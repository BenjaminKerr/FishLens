# ****************************************************************
# File: Trainer.py
# Description: Train a CNN model for fish classification
#   Currently using an extremely small dataset of fish images
#   stored in Classifier/data/train with subfolders for each class.
#   Needs to be rerun everytime there are changes to the dataset.
#   Creates the fish_classifier_model.h5 file used by the classification.py script.
# Author:   Reid
# Contributors: Aleks
# Notes: At time of writing, needs python version 3.12 as tensorflow does not support python 3.13+
# ****************************************************************

import tensorflow as tf
from keras import layers, Sequential, callbacks
from keras.optimizers import Adam
import os

# ========================================================================
# CONFIGURATION AND CONSTANTS
# ========================================================================

# File paths and directories
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PARENT_DIR = os.path.dirname(BASE_DIR)
EXPORT_PATH = os.path.join(PARENT_DIR, 'fish_classifier_model.h5')
TRAIN_DIR = os.path.join(BASE_DIR, 'data', 'train')

# Data Constants
SHEAR = 0.2 # % of total width to randomly shear the image
ZOOM = 0.3 # % of total size to randomly zoom the image
BRIGHTNESS = [0.5, 1.5] # Range for random brightness adjustment (50% darker to 50% brighter)
WIDTH_SHIFT = 0.1 # % of total width to randomly shift the image left and right
HEIGHT_SHIFT = 0.1 # % of total height to randomly shift the image up and down
RANDOM_BRIGHTNESS = 0.5 # Maximum brightness adjustment factor (50% darker to 50% brighter)
VALIDATION_SPLIT = 0.15 # % of data to use for validation
IMAGE_SIZE = 150 # in pixels
BATCH_SIZE = 8 # Number of images to process in a batch
EPOCHS = 30 # Number of times to iterate over the entire dataset
RANDOM_SEED = 42 # Random seed for reproducibility of shuffling and train/validation split

# Model Constants
# Dropout: regularization technique that randomly sets a fraction of input units to 0 during training to prevent overfitting.
#   **Keep between 0.4-0.6
# Learning rate: hyperparameter that controls how much to change the model in response to estimated error.
#   **Keep between 1e-4 and 5e-5
# Patience: number of epochs with no improvement after which training will be stopped.
#   **Keep between 10 and 20
DROPOUT_RATE = 0.5
LEARNING_RATE = 5e-5
PATIENCE = 15

# ========================================================================
# DATA AUGMENTATION
# ========================================================================

# ******************************
# Function: Data Augmentation Layers
# Description: Creates augmentation transformations to be applied during training.
#   These layers apply various transformations to images to increase training data diversity.
# Transformations applied:
#   RandomFlip        - Randomly flips images horizontally
#   RandomRotation    - Randomly rotates images (shear range)
#   RandomZoom        - Randomly zooms images
#   RandomTranslation - Randomly shifts images left/right and up/down
#   RandomBrightness  - Randomly adjusts brightness
augmentation = Sequential([
    layers.RandomFlip("horizontal"),
    layers.RandomRotation(SHEAR),
    layers.RandomZoom(ZOOM),
    layers.RandomTranslation(HEIGHT_SHIFT, WIDTH_SHIFT),
    layers.RandomBrightness(RANDOM_BRIGHTNESS),
], name="data_augmentation")

# ******************************
# Function: augment_data
# Description: Applies the defined augmentation transformations to a batch of images and labels.
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
# Description: Loads training images from directory using tf.keras.utils.image_dataset_from_directory.
#   This is the modern approach replacing legacy ImageDataGenerator.
# Returns a tf.data.Dataset for efficient pipeline processing
# Input:
#   TRAIN_DIR         - Directory where training images are stored in class subfolders
#   image_size        - Resizes all images to the specified dimensions (e.g., 150x150 pixels)
#   batch_size        - Number of images to process in a batch
#   validation_split  - Reserves a portion of training data for validation
#   subset            - Specifies this is the training subset
#   seed              - Random seed for reproducible shuffling
train_dataset = tf.keras.utils.image_dataset_from_directory(
    TRAIN_DIR,
    labels='inferred',
    label_mode='categorical',
    image_size=(IMAGE_SIZE, IMAGE_SIZE),
    batch_size=BATCH_SIZE,
    validation_split=VALIDATION_SPLIT,
    subset='training',
    seed=RANDOM_SEED
)

# ******************************
# Function: Load Validation Dataset
# Description: Loads validation images from directory using tf.keras.utils.image_dataset_from_directory.
#   This is the modern approach replacing legacy ImageDataGenerator.
# Returns a tf.data.Dataset for efficient pipeline processing
# Input:
#   TRAIN_DIR         - Directory where training images are stored in class subfolders
#   image_size        - Resizes all images to the specified dimensions (e.g., 150x150 pixels)
#   batch_size        - Number of images to process in a batch
#   validation_split  - Reserves a portion of training data for validation
#   subset            - Specifies this is the validation subset
#   seed              - Random seed for reproducible shuffling
validation_dataset = tf.keras.utils.image_dataset_from_directory(
    TRAIN_DIR,
    labels='inferred',
    label_mode='categorical',
    image_size=(IMAGE_SIZE, IMAGE_SIZE),
    batch_size=BATCH_SIZE,
    validation_split=VALIDATION_SPLIT,
    subset='validation',
    seed=RANDOM_SEED
)

# ========================================================================
# DATA PROCESSING
# ========================================================================

# ******************************
# Function: Extract Class Metadata
# Description: Extracts class names and counts from the dataset.
# Extract class information
class_names = train_dataset.class_names
num_classes = len(class_names)
print(f"Class indices: {dict(enumerate(class_names))}")

# ******************************
# Function: Create Normalization Layer
# Description: Rescales pixel values from [0, 255] to [0, 1] range for better model training.
# Create normalization layer
normalization_layer = layers.Rescaling(1./255)

# Apply augmentation to training data only
train_dataset = train_dataset.map(augment_data)

# Apply normalization to both datasets
train_dataset = train_dataset.map(lambda x, y: (normalization_layer(x), y))
validation_dataset = validation_dataset.map(lambda x, y: (normalization_layer(x), y))

# Prefetch datasets for optimal performance
train_dataset = train_dataset.prefetch(tf.data.AUTOTUNE)
validation_dataset = validation_dataset.prefetch(tf.data.AUTOTUNE)

# ========================================================================
# MODEL DEFINITION
# ========================================================================

# ******************************
# Function: Build CNN Model
# Description: Creates a Convolutional Neural Network (CNN) model using Keras Sequential API.
# Architecture Details:
#   Conv2D        - Convolutional layers that apply filters to extract features from images
#   BatchNorm     - Normalizes layer inputs to improve training stability
#   MaxPooling2D  - Reduces spatial dimensions by taking maximum value in a window
#   Flatten       - Converts 2D feature maps into a 1D array for dense layers
#   Dense         - Fully connected layers for classification
#   Dropout       - Regularization technique to prevent overfitting
model = Sequential([
    # First convolutional block
    layers.Conv2D(32, (3, 3), activation='relu', input_shape=(IMAGE_SIZE, IMAGE_SIZE, 3)),
    layers.BatchNormalization(),
    layers.MaxPooling2D((2, 2)),
    
    # Second convolutional block
    layers.Conv2D(64, (3, 3), activation='relu'),
    layers.BatchNormalization(),
    layers.MaxPooling2D((2, 2)),
    
    # Dense layers
    layers.Flatten(),
    layers.Dense(256, activation='relu'),
    layers.Dropout(DROPOUT_RATE),
    layers.Dense(num_classes, activation='softmax') 
])

# ========================================================================
# MODEL COMPILATION
# ========================================================================

# ******************************
# Function: Compile Model
# Description: Configures the model for training by specifying optimizer, loss function, and metrics.
# Settings:
#   optimizer - 'Adam' is an industry-standard optimizer known for being fast and reliable
#   loss      - 'categorical_crossentropy' for multi-class classification
#   metrics   - 'accuracy' measures the proportion of correctly classified samples
model.compile(
    optimizer=Adam(learning_rate=LEARNING_RATE),
    loss='categorical_crossentropy',
    metrics=['accuracy']
)

# ========================================================================
# MODEL TRAINING
# ========================================================================

# ******************************
# Function: Configure Training Callbacks
# Description: Sets up early stopping to prevent overfitting by monitoring validation loss.
# Create early stopping callback to prevent overfitting
early_stop = callbacks.EarlyStopping(
    monitor='val_loss',
    patience=PATIENCE,
    restore_best_weights=True
)

# ******************************
# Function: Train Model
# Description: Fits the model to training data using the tf.data.Dataset pipeline.
# Inputs:
#   train_dataset      - tf.data.Dataset providing batches of training data with augmentation
#   epochs             - Number of complete passes through the training dataset
#   validation_data    - tf.data.Dataset for evaluating model performance after each epoch
#   callbacks          - List of callbacks including EarlyStopping to prevent overfitting
# Note: tf.data.Dataset is more efficient than legacy generators and supports eager execution.
# Train the model
history = model.fit(
    train_dataset,
    epochs=EPOCHS,
    validation_data=validation_dataset,
    callbacks=[early_stop]
)

# ========================================================================
# MODEL SAVING
# ========================================================================

# ******************************
# Function: Save Trained Model
# Description: Saves the trained model to disk in HDF5 format for later use.
model.save(EXPORT_PATH)
print("Model saved successfully as fish_classifier_model.h5")