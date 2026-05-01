# ****************************************************************
# File: Trainer2.py
# Description: Train a MobileNetV2-based transfer learning model
#   for fish classification using explicit train/validation folders.
#   This script is intended to be a stronger alternative to trainer.py
#   while preserving a similar workflow and comment style.
#   Creates the fish_classifier_model.keras file used by the classification.py script.
# Author:   Reid
# Contributors: Aleks, Codex
# Notes:
#   - Expects images in Classifier/data/train/<ClassName> and
#     Classifier/data/val/<ClassName>
#   - Uses ImageNet-pretrained MobileNetV2 weights
#   - First trains a new classification head with the base frozen,
#     then fine-tunes the last few MobileNetV2 layers at a very low LR
# ****************************************************************

import os
import numpy as np
import tensorflow as tf
from keras import layers, callbacks, Model, Sequential
from keras.optimizers import Adam
from keras.applications import MobileNetV2
from keras.models import load_model

# ========================================================================
# CONFIGURATION AND CONSTANTS
# ========================================================================

# File paths and directories
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PARENT_DIR = os.path.dirname(BASE_DIR)
EXPORT_PATH = os.path.join(PARENT_DIR, "models", "fish_classifier_model.keras")
TRAIN_DIR = os.path.join(BASE_DIR, "data", "train")
VAL_DIR = os.path.join(BASE_DIR, "data", "val")

# Data Constants
IMAGE_SIZE = 160  # MobileNetV2 works well with 160x160 or 224x224 inputs
BATCH_SIZE = 8
INITIAL_EPOCHS = 12  # Train the new classifier head first
FINE_TUNE_EPOCHS = 20  # Then fine-tune part of the base model
SHUFFLE_BUFFER = 1000

# Augmentation Constants
ROTATION = 0.08
ZOOM = 0.10
WIDTH_SHIFT = 0.08
HEIGHT_SHIFT = 0.08
RANDOM_BRIGHTNESS = 0.15
RANDOM_CONTRAST = 0.08

# Model / Training Constants
HEAD_DROPOUT_RATE = 0.30
INITIAL_LEARNING_RATE = 1e-4
FINE_TUNE_LEARNING_RATE = 1e-5
PATIENCE = 6
FINE_TUNE_AT = 20  # Unfreeze the last N layers of MobileNetV2

# ========================================================================
# DATA AUGMENTATION
# ========================================================================

# ******************************
# Function: Data Augmentation Layers
# Description: Creates augmentation transformations to be applied during training.
#   These layers add moderate variability without heavily distorting the fish.
augmentation = Sequential([
    layers.RandomFlip("horizontal"),
    layers.RandomRotation(ROTATION),
    layers.RandomZoom(ZOOM),
    layers.RandomTranslation(HEIGHT_SHIFT, WIDTH_SHIFT),
    layers.RandomBrightness(RANDOM_BRIGHTNESS),
    layers.RandomContrast(RANDOM_CONTRAST),
], name="data_augmentation")


# ******************************
# Function: augment_data
# Description: Applies augmentation to training images only.
# Input:
#   image - A batch of images to be augmented
#   label - Corresponding labels for the images
# Output: Augmented image batch with original labels
def augment_data(image, label):
    """Apply data augmentation transformations to training images."""
    return augmentation(image, training=True), label


# ========================================================================
# DATA LOADING
# ========================================================================

# ******************************
# Function: Load Training Dataset
# Description: Loads training images from the explicit train folder.
train_dataset = tf.keras.utils.image_dataset_from_directory(
    TRAIN_DIR,
    labels="inferred",
    label_mode="categorical",
    image_size=(IMAGE_SIZE, IMAGE_SIZE),
    batch_size=BATCH_SIZE,
    shuffle=True
)

# ******************************
# Function: Load Validation Dataset
# Description: Loads validation images from the explicit validation folder.
validation_dataset = tf.keras.utils.image_dataset_from_directory(
    VAL_DIR,
    labels="inferred",
    label_mode="categorical",
    image_size=(IMAGE_SIZE, IMAGE_SIZE),
    batch_size=BATCH_SIZE,
    shuffle=False
)


# ========================================================================
# DATA PROCESSING
# ========================================================================

# ******************************
# Function: Extract Class Metadata
# Description: Extracts class names and counts from the dataset.
class_names = train_dataset.class_names
num_classes = len(class_names)
print(f"Class indices: {dict(enumerate(class_names))}")

# Shuffle and prefetch datasets for better training throughput.
# Augmentation is applied inside the model so validation images stay untouched.
train_dataset = train_dataset.shuffle(SHUFFLE_BUFFER).prefetch(tf.data.AUTOTUNE)
validation_dataset = validation_dataset.prefetch(tf.data.AUTOTUNE)


# ******************************
# Function: print_confusion_matrix
# Description: Runs the trained model against the validation dataset and prints
#   a simple confusion matrix so we can see how often each fish type is predicted.
def print_confusion_matrix(model, validation_dataset, class_names):
    x_batches = []
    y_batches = []

    for images, labels in validation_dataset:
        x_batches.append(images.numpy())
        y_batches.append(labels.numpy())

    if not x_batches or not y_batches:
        print("Validation confusion matrix unavailable: validation dataset is empty.")
        return

    x_val = np.concatenate(x_batches, axis=0)
    y_val = np.concatenate(y_batches, axis=0)

    predictions = model.predict(x_val, verbose=0)
    y_true = np.argmax(y_val, axis=1)
    y_pred = np.argmax(predictions, axis=1)

    matrix = np.zeros((len(class_names), len(class_names)), dtype=int)
    for truth, pred in zip(y_true, y_pred):
        matrix[truth, pred] += 1

    print("\nValidation confusion matrix")
    print("rows=true, cols=pred")
    print(" " * 14 + " ".join(f"{name:>10s}" for name in class_names))
    for row_index, class_name in enumerate(class_names):
        row_values = " ".join(f"{value:10d}" for value in matrix[row_index])
        print(f"{class_name:>12s}  {row_values}")


# ========================================================================
# MODEL DEFINITION
# ========================================================================

# ******************************
# Function: Build Base MobileNetV2 Model
# Description: Loads a small ImageNet-pretrained backbone without its top classifier.
# Notes:
#   - include_top=False removes the original ImageNet classification layers
#   - weights='imagenet' uses pre-trained visual features as a starting point
base_model = MobileNetV2(
    input_shape=(IMAGE_SIZE, IMAGE_SIZE, 3),
    include_top=False,
    weights="imagenet"
)

# Freeze the entire base model for the first training phase.
base_model.trainable = False


# ******************************
# Function: Build Transfer Learning Model
# Description: Creates a classifier head on top of MobileNetV2.
# Architecture Details:
#   Input                 - Raw RGB image tensor
#   Data Augmentation     - Moderate train-time image transforms
#   Rescaling             - MobileNetV2-style normalization from [0,255] to [-1,1]
#   MobileNetV2           - Pre-trained feature extractor
#   GlobalAveragePooling2D- Converts feature maps into a compact feature vector
#   Dropout               - Regularization to reduce overfitting
#   Dense                 - Final classifier for fish species
inputs = layers.Input(shape=(IMAGE_SIZE, IMAGE_SIZE, 3))
x = augmentation(inputs)
x = layers.Rescaling(scale=1.0 / 127.5, offset=-1)(x)
x = base_model(x, training=False)
x = layers.GlobalAveragePooling2D()(x)
x = layers.Dropout(HEAD_DROPOUT_RATE)(x)
outputs = layers.Dense(num_classes, activation="softmax")(x)
model = Model(inputs, outputs)


# ========================================================================
# MODEL COMPILATION
# ========================================================================

# ******************************
# Function: Compile Initial Model
# Description: Configures the frozen-base model for head training.
model.compile(
    optimizer=Adam(learning_rate=INITIAL_LEARNING_RATE),
    loss="categorical_crossentropy",
    metrics=["accuracy"]
)


# ========================================================================
# MODEL TRAINING - PHASE 1
# ========================================================================

# ******************************
# Function: Configure Initial Training Callbacks
# Description: Saves the best validation model, stops early if validation loss
#   stops improving, and gently reduces the learning rate when performance plateaus.
best_model_checkpoint = callbacks.ModelCheckpoint(
    filepath=EXPORT_PATH,
    monitor="val_loss",
    save_best_only=True,
    save_weights_only=False,
    verbose=1
)

early_stop = callbacks.EarlyStopping(
    monitor="val_loss",
    patience=PATIENCE,
    restore_best_weights=True
)

reduce_lr = callbacks.ReduceLROnPlateau(
    monitor="val_loss",
    factor=0.5,
    patience=3,
    min_lr=1e-6,
    verbose=1
)

# ******************************
# Function: Train Classifier Head
# Description: Trains only the new layers on top of the frozen MobileNetV2 base.
history_initial = model.fit(
    train_dataset,
    epochs=INITIAL_EPOCHS,
    validation_data=validation_dataset,
    callbacks=[best_model_checkpoint, early_stop, reduce_lr]
)


# ========================================================================
# MODEL FINE-TUNING - PHASE 2
# ========================================================================

# ******************************
# Function: Unfreeze Top MobileNetV2 Layers
# Description: Opens up only the last few layers for careful fine-tuning.
# Notes:
#   - Keep most of the backbone frozen to avoid destroying useful pre-trained features
#   - Recompile after changing layer trainability
base_model.trainable = True

for layer in base_model.layers[:-FINE_TUNE_AT]:
    layer.trainable = False

# ******************************
# Function: Recompile for Fine-Tuning
# Description: Uses a much lower learning rate for the second phase.
model.compile(
    optimizer=Adam(learning_rate=FINE_TUNE_LEARNING_RATE),
    loss="categorical_crossentropy",
    metrics=["accuracy"]
)

# ******************************
# Function: Fine-Tune Model
# Description: Carefully adapts the last MobileNetV2 layers to the fish dataset.
history_fine = model.fit(
    train_dataset,
    epochs=INITIAL_EPOCHS + FINE_TUNE_EPOCHS,
    initial_epoch=len(history_initial.history["loss"]),
    validation_data=validation_dataset,
    callbacks=[best_model_checkpoint, early_stop, reduce_lr]
)


# ========================================================================
# MODEL SAVING
# ========================================================================

# ******************************
# Function: Save Final Model
# Description: Reloads the best validation checkpoint from disk and reports metrics from it.
best_model = load_model(EXPORT_PATH, compile=False)
print("Model saved successfully as fish_classifier_model.keras")
print_confusion_matrix(best_model, validation_dataset, class_names)
