// ***************************************************************************************************************************
// File: VideoProgressSelector.cs
// Description: Provides templated format for progress bar states.
// Notes: N/A
// ***************************************************************************************************************************
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FishLens_App
{
    public class VideoProgressSelector : DataTemplateSelector
    {
        public DataTemplate FilledTemplate { get; set; }
        public DataTemplate EmptyTemplate { get; set; }
        public DataTemplate ActiveTemplate { get; set; }

        // **************************************************
        // Function: SelectTemplate
        // Description: Called to dynamically switch the state
        // of a progress rectangle.
        // Notes: N/A
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is MainWindow.VideoProgressState state)
            {
                return state switch
                {
                    MainWindow.VideoProgressState.Filled => FilledTemplate,
                    MainWindow.VideoProgressState.Active => ActiveTemplate,
                    _ => EmptyTemplate
                };
            }

            return base.SelectTemplate(item, container);
        }
    }
}
