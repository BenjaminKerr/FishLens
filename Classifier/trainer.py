# ****************************************************************
# File: Trainer.py
# Purpose: Train a CNN model for fish classification
#   Currently using an extremely small dataset of fish images
#   stored in Classifier/data/train with subfolders for each class.
#   Needs to be rerun everytime there are changes to the dataset.
#   Creates the fish_classifier_model.h5 file used by the classification.py script.
# Notes:
#   At time of writing, needs python version 3.12 as tensorflow does not support python 3.13+

from tensorflow.keras.preprocessing.image import ImageDataGenerator # type : ignore
from keras.models import Sequential
from keras.layers import Conv2D, MaxPooling2D, Flatten, Dense
import os

# This is the folder where the script lives
BASE_DIR = os.path.dirname(os.path.abspath(__file__))

train_dir = os.path.join(BASE_DIR, 'data', 'train')  

train_datagen = ImageDataGenerator(
    rescale=1./255,
    shear_range=0.2,
    zoom_range=0.2,
    horizontal_flip=True,
    validation_split=0.2  
)

train_generator = train_datagen.flow_from_directory(
    train_dir,
    target_size=(150, 150),
    batch_size=32,
    class_mode='categorical',
    subset='validation'
)

# 3. Load Validation Data
validation_generator = train_datagen.flow_from_directory(
    train_dir,
    target_size=(150, 150),
    batch_size=32,
    class_mode='categorical',
    subset='validation'             # Specify this is the validation subset
)

# You can now access the class names in the generator
print(train_generator.class_indices)

# Get the number of output classes from the generator
num_classes = train_generator.num_classes
IMAGE_SIZE = 150

model = Sequential([
    # Input Layer (150x150, 3 channels)
    Conv2D(32, (3, 3), activation='relu', input_shape=(IMAGE_SIZE, IMAGE_SIZE, 3)),
    MaxPooling2D(2, 2),
    
    # Hidden Layers
    Conv2D(64, (3, 3), activation='relu'),
    MaxPooling2D(2, 2),
    
    # Classification Head
    Flatten(),
    Dense(128, activation='relu'),
    # Output layer uses 'softmax' for multi-class prediction
    Dense(num_classes, activation='softmax') 
])

# Compile: Define the optimizer and loss function
model.compile(
    optimizer='adam',
    loss='categorical_crossentropy', # Appropriate for multi-class labels
    metrics=['accuracy']
)

# Train: Fit the model to the data generators
# steps_per_epoch ensures you cycle through all training images once per epoch
history = model.fit(
    train_generator,
    steps_per_epoch=train_generator.samples // train_generator.batch_size,
    epochs=50, # You can adjust this number, can backfire changing it though
    validation_data=validation_generator,
    validation_steps=validation_generator.samples // validation_generator.batch_size
)

# Defines the saved model file for use
model.save("fish_classifier_model.h5")
# Only runs as a confirmation that the model was saved
print("Model saved successfully as fish_classifier_model.h5")