namespace FishLens_App.Models
{
    // **************************************************
    // Function: CheckBoxToggle
    // Description: Small DTO (Data Transfer Object) that stores UI toggle state for output and error redirection.
    // Note: Data Transfer Object - A simple object used to transfer data between different parts
    //       of your application.
    // **************************************************
    public class CheckBoxToggle
    {
        public bool OutputBox { get; set; }
        public bool ErrorBox { get; set; }
        public bool FastMode { get; set; }
        public bool ForceReanalyze { get; set; }
    }
}
