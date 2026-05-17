using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public class VideoProgressStatus : INotifyPropertyChanged
    {
        public int Pid { get; set; }
        public string Status { get; set; }
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
