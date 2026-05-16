using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    internal class VideoProgressStatus : INotifyPropertyChanged
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
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(Message)));
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;

    }
}
