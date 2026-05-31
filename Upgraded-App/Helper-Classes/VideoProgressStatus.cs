using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public enum VideoProgressState
    {
        Empty,
        InProgress,
        Filled

    }
    public class VideoProgressStatus : INotifyPropertyChanged
    {
       public int Pid { get; set;  }


        private VideoProgressState _state;

        public int VideoIndex {  get; set; }
        public VideoProgressState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                _state = value;
                OnPropertyChanged(nameof(State));
            }
        }

        public string Filename { get; set; }

        private string _message;
        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                Debug.WriteLine($"[DEBUG] Updated PID={Pid} → \"{_message}\"");
                OnPropertyChanged(nameof(Message));
            }
        }

        public void SetInProgress()
        {
            State = VideoProgressState.InProgress;
        }
        public void SetComplete()
        {
            State = VideoProgressState.Filled;
        }
        public void SetEmpty()
        {
            State = VideoProgressState.Empty;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
