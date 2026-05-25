# ****************************************************************
# File: sweep_thresholds.py
# Description: Sweep species confidence/margin thresholds on validation data
#   to find settings that maximize macro-F1 while preserving coverage.
# Author: Codex
# ****************************************************************

import os
import sys
from collections import defaultdict

import numpy as np

from test_model import (
    MODEL_PATH,
    FALLBACK_MODEL_PATH,
    VAL_DIR,
    SUPPORTED_EXTENSIONS,
    get_model_input_size,
    get_preprocess_mode,
)


def _resolve_model_path():
    if os.path.exists(MODEL_PATH):
        return MODEL_PATH
    if os.path.exists(FALLBACK_MODEL_PATH):
        return FALLBACK_MODEL_PATH
    raise FileNotFoundError("No classifier model found in models/.")


def _try_enable_torch_keras_backend(last_error=None):
    if str(os.getenv("KERAS_BACKEND", "")).strip():
        return False

    message = str(last_error) if last_error is not None else ""
    if "tensorflow" not in message.lower():
        return False

    os.environ["KERAS_BACKEND"] = "torch"
    for module_name in list(sys.modules.keys()):
        if module_name == "keras" or module_name.startswith("keras."):
            sys.modules.pop(module_name, None)
    return True


def _load_model_with_fallback(model_path):
    attempts = [
        ("tensorflow.keras.models", {"compile": False}),
        ("keras.models", {"compile": False}),
        ("tensorflow.keras.models", {}),
        ("keras.models", {}),
    ]

    last_error = None
    for module_name, kwargs in attempts:
        try:
            keras_models = __import__(module_name, fromlist=["load_model"])
            return keras_models.load_model(model_path, **kwargs)
        except Exception as exc:
            last_error = exc

    if _try_enable_torch_keras_backend(last_error):
        for module_name, kwargs in (("keras.models", {"compile": False}), ("keras.models", {})):
            try:
                keras_models = __import__(module_name, fromlist=["load_model"])
                print("[INFO] Using Keras torch backend.")
                return keras_models.load_model(model_path, **kwargs)
            except Exception as exc:
                last_error = exc

    raise RuntimeError(f"Unable to load classifier model: {last_error}")


def iter_validation_images(val_dir):
    for class_name in sorted(os.listdir(val_dir)):
        class_dir = os.path.join(val_dir, class_name)
        if not os.path.isdir(class_dir):
            continue

        for filename in sorted(os.listdir(class_dir)):
            path = os.path.join(class_dir, filename)
            if not os.path.isfile(path):
                continue
            _, ext = os.path.splitext(filename)
            if ext.lower() in SUPPORTED_EXTENSIONS:
                yield class_name, path


def _prepare_classifier_array(img_array, preprocess_mode):
    if preprocess_mode == "mobilenet_v2":
        img_array = (img_array / 127.5) - 1.0
    elif preprocess_mode == "zero_one":
        img_array = img_array / 255.0
    return np.expand_dims(img_array, axis=0)


def _tta_views(rgb_image):
    views = [rgb_image]
    views.append(np.ascontiguousarray(np.flip(rgb_image, axis=1)))

    h, w = rgb_image.shape[:2]
    crop_ratio = 0.88
    crop_h = max(1, int(h * crop_ratio))
    crop_w = max(1, int(w * crop_ratio))
    y0 = max(0, (h - crop_h) // 2)
    x0 = max(0, (w - crop_w) // 2)
    center_crop = rgb_image[y0:y0 + crop_h, x0:x0 + crop_w]
    if center_crop.size > 0:
        center_crop = np.array(
            __import__("cv2").resize(center_crop, (w, h), interpolation=__import__("cv2").INTER_LINEAR),
            dtype=np.uint8,
        )
        views.append(center_crop)

    return views


def predict_probs_with_tta(model, image_path, image_size, preprocess_mode):
    keras_image = __import__("keras.preprocessing.image", fromlist=["load_img", "img_to_array"])
    load_img = keras_image.load_img
    img_to_array = keras_image.img_to_array

    image = load_img(image_path, target_size=image_size)
    rgb = np.asarray(img_to_array(image), dtype=np.uint8)

    probs_list = []
    for view in _tta_views(rgb):
        batch = _prepare_classifier_array(np.asarray(view, dtype=np.float32), preprocess_mode)
        predictions = model.predict(batch, verbose=0)
        probs = predictions[0] if predictions is not None and len(predictions) > 0 else None
        if probs is None or len(probs) == 0:
            continue
        probs_list.append(np.asarray(probs, dtype=np.float32))

    if not probs_list:
        return None

    return np.mean(np.stack(probs_list, axis=0), axis=0)


def compute_macro_f1(labels, preds, class_names):
    f1_values = []
    for class_name in class_names:
        tp = sum(1 for t, p in zip(labels, preds) if t == class_name and p == class_name)
        fp = sum(1 for t, p in zip(labels, preds) if t != class_name and p == class_name)
        fn = sum(1 for t, p in zip(labels, preds) if t == class_name and p != class_name)

        precision = tp / (tp + fp) if (tp + fp) else 0.0
        recall = tp / (tp + fn) if (tp + fn) else 0.0
        f1 = (2.0 * precision * recall / (precision + recall)) if (precision + recall) else 0.0
        f1_values.append(f1)

    return float(np.mean(f1_values)) if f1_values else 0.0


def evaluate_thresholds(records, conf_th, margin_th, class_names):
    labels = []
    preds = []

    accepted = 0
    uncertain = 0
    for item in records:
        probs = item["probs"]
        pred_idx = int(np.argmax(probs))
        sorted_probs = np.sort(probs)
        best = float(probs[pred_idx])
        second = float(sorted_probs[-2]) if len(sorted_probs) >= 2 else 0.0
        margin = max(0.0, best - second)

        labels.append(item["true_label"])

        if best >= conf_th and margin >= margin_th:
            preds.append(class_names[pred_idx])
            accepted += 1
        else:
            preds.append("Uncertain")
            uncertain += 1

    decided_pairs = [(t, p) for t, p in zip(labels, preds) if p != "Uncertain"]
    if decided_pairs:
        decided_true = [t for t, _ in decided_pairs]
        decided_pred = [p for _, p in decided_pairs]
        macro_f1 = compute_macro_f1(decided_true, decided_pred, class_names)
    else:
        macro_f1 = 0.0

    coverage = accepted / max(1, len(records))
    return {
        "conf": conf_th,
        "margin": margin_th,
        "macro_f1": macro_f1,
        "coverage": coverage,
        "accepted": accepted,
        "uncertain": uncertain,
    }


def main():
    model_path = _resolve_model_path()
    model = _load_model_with_fallback(model_path)

    image_size = get_model_input_size(model)
    preprocess_mode = get_preprocess_mode(model)

    class_names = [
        name for name in sorted(os.listdir(VAL_DIR))
        if os.path.isdir(os.path.join(VAL_DIR, name))
    ]

    records = []
    failures = []
    for true_label, image_path in iter_validation_images(VAL_DIR):
        try:
            probs = predict_probs_with_tta(model, image_path, image_size, preprocess_mode)
            if probs is None:
                failures.append(image_path)
                continue
            records.append({"true_label": true_label, "probs": probs, "image_path": image_path})
        except Exception:
            failures.append(image_path)

    if not records:
        raise RuntimeError("No validation predictions were produced.")

    conf_values = np.arange(0.50, 0.91, 0.02)
    margin_values = np.arange(0.04, 0.31, 0.02)

    results = []
    for conf_th in conf_values:
        for margin_th in margin_values:
            results.append(evaluate_thresholds(records, float(conf_th), float(margin_th), class_names))

    # Prefer high macro-F1 first, then higher coverage.
    best = sorted(results, key=lambda r: (r["macro_f1"], r["coverage"]), reverse=True)[0]

    print(f"Model: {model_path}")
    print(f"Image size: {image_size} | preprocess: {preprocess_mode}")
    print(f"Validation samples scored: {len(records)}")
    print(f"Validation failures: {len(failures)}")
    print("\nBest thresholds")
    print(f"  confidence >= {best['conf']:.2f}")
    print(f"  margin >= {best['margin']:.2f}")
    print(f"  macro_f1 = {best['macro_f1']:.4f}")
    print(f"  coverage = {best['coverage']:.4f} ({best['accepted']}/{len(records)})")

    print("\nTop 8 threshold candidates")
    top = sorted(results, key=lambda r: (r["macro_f1"], r["coverage"]), reverse=True)[:8]
    for row in top:
        print(
            f"  conf={row['conf']:.2f} margin={row['margin']:.2f} "
            f"f1={row['macro_f1']:.4f} coverage={row['coverage']:.4f}"
        )


if __name__ == "__main__":
    main()
