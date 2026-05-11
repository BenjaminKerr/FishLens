# ****************************************************************
# File: test_model.py
# Description: Quick validation-set sanity check for the currently saved
#   fish classifier model. Loads the model from models/ and runs it against
#   Classifier/data/val/<ClassName>, then prints per-image predictions and
#   a confusion matrix so incorrect Chinook/Omykiss guesses are easy to spot.
# Author: Reid
# Contributors: Codex
# ****************************************************************

import os
from collections import defaultdict

import numpy as np
from keras.models import load_model
from keras.preprocessing.image import load_img, img_to_array


# ========================================================================
# CONFIGURATION AND CONSTANTS
# ========================================================================

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PARENT_DIR = os.path.dirname(BASE_DIR)
VAL_DIR = os.path.join(BASE_DIR, "data", "val")
MODEL_PATH = os.path.join(PARENT_DIR, "models", "fish_classifier_model.keras")
FALLBACK_MODEL_PATH = os.path.join(PARENT_DIR, "models", "fish_classifier_model.h5")
SUPPORTED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}


# ========================================================================
# HELPER FUNCTIONS
# ========================================================================

def resolve_model_path():
    """Prefer the native Keras model, with HDF5 as a fallback."""
    if os.path.exists(MODEL_PATH):
        return MODEL_PATH
    if os.path.exists(FALLBACK_MODEL_PATH):
        return FALLBACK_MODEL_PATH
    raise FileNotFoundError("No classifier model found in models/.")


def get_model_input_size(model, default=(150, 150)):
    """Infer expected image size from the model input shape."""
    shape = getattr(model, "input_shape", None)
    if shape and len(shape) >= 3 and shape[1] and shape[2]:
        return int(shape[1]), int(shape[2])
    return default


def get_preprocess_mode(model):
    """Return the expected caller-side preprocessing mode for the loaded classifier."""
    layer_names = [str(getattr(layer, "name", "")).lower() for layer in getattr(model, "layers", [])]
    has_mobilenet_backbone = any("mobilenet" in name for name in layer_names)
    has_internal_rescaling = any(name.startswith("rescaling") for name in layer_names)
    if has_internal_rescaling:
        return "raw_255"
    if has_mobilenet_backbone:
        return "mobilenet_v2"
    return "zero_one"


def iter_validation_images(val_dir):
    """Yield (true_label, image_path) pairs from Classifier/data/val."""
    for class_name in sorted(os.listdir(val_dir)):
        class_dir = os.path.join(val_dir, class_name)
        if not os.path.isdir(class_dir):
            continue

        for filename in sorted(os.listdir(class_dir)):
            image_path = os.path.join(class_dir, filename)
            if not os.path.isfile(image_path):
                continue

            _, ext = os.path.splitext(filename)
            if ext.lower() in SUPPORTED_EXTENSIONS:
                yield class_name, image_path


def preprocess_image(image_path, image_size, preprocess_mode):
    """Load and normalize one image using the app/trainer-compatible path."""
    img = load_img(image_path, target_size=image_size)
    img_array = img_to_array(img)

    if preprocess_mode == "mobilenet_v2":
        img_array = (img_array / 127.5) - 1.0
    elif preprocess_mode == "zero_one":
        img_array = img_array / 255.0

    return np.expand_dims(img_array, axis=0)


def print_confusion_matrix(class_names, results):
    """Print a simple confusion matrix from test results."""
    matrix = np.zeros((len(class_names), len(class_names)), dtype=int)
    class_to_index = {name: idx for idx, name in enumerate(class_names)}

    for item in results:
        truth_idx = class_to_index[item["true_label"]]
        pred_idx = class_to_index[item["pred_label"]]
        matrix[truth_idx, pred_idx] += 1

    print("\nValidation confusion matrix")
    print("rows=true, cols=pred")
    print(" " * 14 + " ".join(f"{name:>10s}" for name in class_names))
    for row_index, class_name in enumerate(class_names):
        row_values = " ".join(f"{value:10d}" for value in matrix[row_index])
        print(f"{class_name:>12s}  {row_values}")


def evaluate_model_on_validation(model, val_dir):
    """Evaluate one loaded model against Classifier/data/val and return a summary dict."""
    image_size = get_model_input_size(model)
    preprocess_mode = get_preprocess_mode(model)

    class_names = [
        name for name in sorted(os.listdir(val_dir))
        if os.path.isdir(os.path.join(val_dir, name))
    ]

    results = []
    wrong_by_true_label = defaultdict(list)

    for true_label, image_path in iter_validation_images(val_dir):
        batch = preprocess_image(image_path, image_size, preprocess_mode)
        predictions = model.predict(batch, verbose=0)[0]
        pred_index = int(np.argmax(predictions))
        pred_label = class_names[pred_index]
        confidence = float(predictions[pred_index])

        result = {
            "true_label": true_label,
            "pred_label": pred_label,
            "confidence": confidence,
            "image_path": image_path,
        }
        results.append(result)

        if pred_label != true_label:
            wrong_by_true_label[true_label].append(result)

    total = len(results)
    correct = sum(1 for item in results if item["true_label"] == item["pred_label"])
    accuracy = (correct / total) if total else 0.0

    return {
        "image_size": image_size,
        "preprocess_mode": preprocess_mode,
        "class_names": class_names,
        "results": results,
        "wrong_by_true_label": wrong_by_true_label,
        "correct": correct,
        "total": total,
        "accuracy": accuracy,
    }


def print_evaluation_summary(model_path, summary):
    """Print a human-readable summary from evaluate_model_on_validation()."""
    print(f"Loaded model: {model_path}")
    print(f"Model input size: {summary['image_size']}")
    print(f"Preprocess mode: {summary['preprocess_mode']}")
    print(f"Validation classes: {summary['class_names']}")
    print(f"\nValidation accuracy: {summary['correct']}/{summary['total']} = {summary['accuracy']:.4f}")
    print_confusion_matrix(summary["class_names"], summary["results"])

    print("\nMisclassified images")
    if not summary["wrong_by_true_label"]:
        print("None")
    else:
        for true_label in summary["class_names"]:
            mistakes = summary["wrong_by_true_label"].get(true_label, [])
            if not mistakes:
                continue
            print(f"\nTrue {true_label}:")
            for item in mistakes:
                filename = os.path.basename(item["image_path"])
                print(
                    f"  {filename} -> {item['pred_label']} "
                    f"({item['confidence']:.4f})"
                )

    print("\nPer-image predictions")
    for item in summary["results"]:
        filename = os.path.basename(item["image_path"])
        marker = "OK" if item["true_label"] == item["pred_label"] else "MISS"
        print(
            f"{marker:4s} true={item['true_label']:<8s} "
            f"pred={item['pred_label']:<8s} "
            f"conf={item['confidence']:.4f} "
            f"file={filename}"
        )


# ========================================================================
# MAIN TEST RUNNER
# ========================================================================

def main():
    model_path = resolve_model_path()
    model = load_model(model_path, compile=False)
    summary = evaluate_model_on_validation(model, VAL_DIR)
    print_evaluation_summary(model_path, summary)


if __name__ == "__main__":
    main()
