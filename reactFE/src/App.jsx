import { useState } from 'react'
import './App.css'

function App() {
  const [name, setName] = useState('')
  const [count, setCount] = useState(0)
  const [items, setItems] = useState(['Item 1', 'Item 2', 'Item 3'])


  // Function to handle video file selection
  const onVideoChange = (event) => {
    setName(event.target.value)
  }


  // Function to handle video upload
  const onVideoUpload = () => {
    const formData = new FormData(); // Create a FormData object
    formData.append('file', name); // Append the selected file to the FormData object

    // Send a POST request to the server with the video file
    
    axios.post('http://localhost:5000/upload', formData)
      .then(response => {
        console.log('Upload successful:', response.data);
      })
      .catch(error => {
        console.error('Upload failed:', error);
      });
  }

  return (
    <div className="App">
      <h1>React Sample App</h1>
      
      {/* Video Input */}
      <div>
        <input 
          type="file"
          placeholder="Drag and drop your video: "
          value={name}
          onChange={onVideoChange}
        />
        <button onClick={onVideoUpload}>Upload</button>
      </div>
      
      {/* Counter */}
      <div>
        <button onClick={() => setCount(count + 1)}>
          Count: {count}
        </button>
      </div>

      {/* List */}
      <div>
        <h3>Items:</h3>
        <ul>
          {items.map((item, index) => (
            <li key={index}>{item}</li>
          ))}
        </ul>
      </div>
    </div>
  )
}

export default App