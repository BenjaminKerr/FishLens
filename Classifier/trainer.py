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

from tensorflow.keras.preprocessing.image import ImageDataGenerator # type : ignore
from keras.models import Sequential
from keras.layers import BatchNormalization, Conv2D, MaxPooling2D, Flatten, Dense, Dropout
from keras.optimizers import Adam
from keras.callbacks import EarlyStopping
import os

# This is the folder where the script lives
# Model will be saved at the project root
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
PARENT_DIR = os.path.dirname(BASE_DIR)
EXPORT_PATH = os.path.join(PARENT_DIR, 'fish_classifier_model.h5')
TRAIN_DIR = os.path.join(BASE_DIR, 'data', 'train')

# Data Constants --
SHEAR = 0.2 # % of total width to randomly shear the image
ZOOM = 0.2 # % of total size to randomly zoom the image
BRIGHTNESS = [0.5, 1.5] # Range for random brightness adjustment (50% darker to 50% brighter)
WIDTH_SHIFT = 0.1 # % of total width to randomly shift the image left and right
HEIGHT_SHIFT = 0.1 # % of total height to randomly shift the image up and down
VALIDATION_SPLIT = 0.2 # % of data to use for validation
IMAGE_SIZE = 150 # in pixels
BATCH_SIZE = 32 # Number of images to process in a batch
EPOCHS = 30 # Number of times to iterate over the entire dataset

# Model Constants --
# Dropout is a regularization technique that randomly sets a fraction of the input units to 0 during training to prevent overfitting.
#   **Keep between 0.3-0.5
# Learning rate is a hyperparameter that controls how much to change the model in response to the estimated error each time the model weights are updated.
#   **Keep between 1e-3 and 1e-5
# Patience is the number of epochs with no improvement after which training will be stopped. This is used in EarlyStopping to prevent overfitting by stopping training when the validation loss stops improving.
#   **Keep between 5 and 10
DROPOUT_RATE = 0.4
LEARNING_RATE = 1e-4
PATIENCE = 7

# Add EarlyStopping to stop training when validation loss stops improving
EARLYSTOP = EarlyStopping(monitor='val_loss', patience=PATIENCE, restore_best_weights=True)

# ******************************
# Function: ImageDataGenerator
# Description: Creates a generator for loading and augmenting images for training and validation.
#   This function applies various transformations to the images to increase the diversity of the training data.
# Input:
#       rescale             - Rescales pixel values to the range [0, 1] instead of [0, 255]
#       shear_range         - Randomly shears the image by a fraction of the total width
#       zoom_range          - Randomly zooms the image by a fraction of the total size
#       brightness_range    - Randomly adjusts the brightness of the image within the specified range (e.g., 50% darker to 50% brighter)
#       width_shift_range   - Randomly shifts the image left and right by a fraction of the total width
#       height_shift_range  - Randomly shifts the image up and down by a fraction of the total height
#       horizontal_flip     - Randomly flips the image horizontally
#       validation_split    - Reserves a portion of the training data for validation
# Output: A generator that can be used to load and augment images for training and validation
train_datagen = ImageDataGenerator(
    rescale=1./255,
    shear_range=SHEAR,
    zoom_range=ZOOM,
    brightness_range=BRIGHTNESS,
    width_shift_range=WIDTH_SHIFT,
    height_shift_range=HEIGHT_SHIFT,
    horizontal_flip=True,
    validation_split=VALIDATION_SPLIT
)

# ******************************
# Function: train_generator
# Description: Loads Training Data from the specified directory using the ImageDataGenerator.
# Inputs:
#   TRAIN_DIR   - The directory where the training images are stored, organized in subfolders for each class.
#   target_size - Resizes all images to the specified dimensions (e.g., 150x150 pixels).
#   batch_size  - The number of images to process in a batch (e.g., 32).
#   class_mode  - Specifies the type of label arrays that are returned (e.g., 'categorical' for multi-class classification).
#   subset      - Specifies that this generator is for the training subset of the data.
train_generator = train_datagen.flow_from_directory(
    TRAIN_DIR,
    target_size=(IMAGE_SIZE, IMAGE_SIZE),
    batch_size=BATCH_SIZE,
    class_mode='categorical',
    subset='training'
)

# ******************************
# Function: validation_generator
# Description: Loads Validation Data from the specified directory using the ImageDataGenerator.
# Inputs:
#   TRAIN_DIR   - The directory where the training images are stored, organized in subfolders for each class.
#   target_size - Resizes all images to the specified dimensions (e.g., 150x150 pixels).
#   batch_size  - The number of images to process in a batch (e.g., 32).
#   class_mode  - Specifies the type of label arrays that are returned (e.g., 'categorical' for multi-class classification).
#   subset      - Specifies that this generator is for the validation subset of the data.
validation_generator = train_datagen.flow_from_directory(
    TRAIN_DIR,
    target_size=(IMAGE_SIZE, IMAGE_SIZE),
    batch_size=BATCH_SIZE,
    class_mode='categorical',
    subset='validation'
)

# Prints class names "{Chinook : 0}, {Omykiss : 1}"
# The numbers are just locations in the array of output classes, not actual labels
print(train_generator.class_indices)

# Get the number of output classes from the generator
num_classes = train_generator.num_classes

# ******************************
# Function: model = Sequential
# Description: Creates a Convolutional Neural Network (CNN) model using Keras Sequential API.
# Inputs:
#   Conv2D          - Convolutional layer that applies 32 filters of size 3x3 with ReLU activation function to the input images.
#   MaxPooling2D    - Pooling layer that reduces the spatial dimensions of the feature maps by taking the maximum value in a 2x2 window.
#   Flatten         - Flattens the 2D feature maps into a 1D array to be fed into the fully connected layers.
#   Dense           - Fully connected layer with 256 neurons and ReLU activation function, followed by an output layer with a number of neurons equal to the number of classes and softmax activation function for multi-class classification.
#   Dropout         - Regularization layer that randomly sets 50% of the input units to 0 during training to prevent overfitting.
model = Sequential([
    Conv2D(32, (3, 3), activation='relu', input_shape=(IMAGE_SIZE, IMAGE_SIZE, 3)),
    BatchNormalization(),
    MaxPooling2D(2, 2),
    
    Conv2D(64, (3, 3), activation='relu'),
    BatchNormalization(),
    MaxPooling2D(2, 2),

    Conv2D(128, (3, 3), activation='relu'),
    BatchNormalization(),
    MaxPooling2D(2, 2),
    
    Flatten(),
    Dense(256, activation='relu'),
    Dropout(DROPOUT_RATE),
    Dense(num_classes, activation='softmax') 
])

# ******************************
# Function: model.compile
# Description: Defines the optimizer, loss function, and evaluation metrics for the model.
# Inputs:
#   optimizer   - 'Adam' is an industry-standard optimizer that is known for being fast and reliable for training deep learning models.
#   loss        - 'categorical_crossentropy' is appropriate for multi-class classification
#   metrics     - 'accuracy' is a common metric for classification tasks that measures the proportion of correctly classified samples.
model.compile(
    optimizer=Adam(learning_rate=LEARNING_RATE),
    loss='categorical_crossentropy',
    metrics=['accuracy']
)

# ******************************
# Function: model.fit
# Description: Fit the model to the training data using the generators for both training and validation.
# Inputs:
#   train_generator        - The generator that provides batches of training data with augmentation.
#   epochs                 - The number of times to iterate over the training data arrays.
#   validation_data        - The generator that provides batches of validation data for evaluating the model's performance after each epoch.
#   callbacks              - List of callbacks to apply during training (e.g., EarlyStopping).
history = model.fit(
    train_generator,
    epochs=EPOCHS,
    validation_data=validation_generator,
    callbacks=[EARLYSTOP]
)

# Defines the saved model file for use
model.save(EXPORT_PATH)

# Only runs as a confirmation that the model was saved
print("Model saved successfully as fish_classifier_model.h5")