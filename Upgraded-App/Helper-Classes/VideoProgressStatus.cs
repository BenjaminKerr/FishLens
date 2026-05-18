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


        private string _status;
        public string Status
        {
            get => _status;
            set
            {
                _status = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
            }
        }
        public VideoProgressState State
        {
            get
            {
                return Status switch
                {
                    "Processing" => VideoProgressState.InProgress,
                    "Complete" => VideoProgressState.Filled,
                         _       => VideoProgressState.Empty
                };
            }
        }
        private string _message;
        public string Message
        {
            get => _message;
            set
            {
                _message = value;
                Debug.WriteLine($"[DEBUG] Updated PID={Pid} → \"{_message}\"");
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Message)));
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;

    }
}
