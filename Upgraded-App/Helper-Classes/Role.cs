// ***************************************************************************************************************************
// File: Role.cs
// Description: This is the class for roles such as Admin or User; it provides a name and identifier used across the app.
// Notes: N/A
// ***************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    public class Role
    {
        public string Name { get; set; }
        public int ID { get; set; }

        // ****************************************************************
        // Function: Role(string name, int id)
        // Description: Initializes a Role with a display name and id.
        // Notes: Used to populate role selectors and to associate permissions.
        public Role(string name, int id)
        {
            Name = name;
            ID = id;
        }

        // ****************************************************************
        // Function: ToString
        // Description: Returns the display name for the role.
        // Notes: Useful for UI bindings and lists where a readable name is required.
        public override string ToString()
        {
            return Name;
        }
    }
}
