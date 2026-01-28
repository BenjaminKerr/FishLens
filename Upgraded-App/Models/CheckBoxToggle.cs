using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App.Models
{
    // **************************************************
    // Function: CheckBoxToggle
    // Description: Small DTO that stores UI toggle state for output and error redirection.
    // **************************************************
    public class CheckBoxToggle
    {
        public bool OutputBox { get; set; } = false;
        public bool ErrorBox { get; set; } = false;
    }
}
