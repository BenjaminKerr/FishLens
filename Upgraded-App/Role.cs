// ***************************************************************************************************************************
// File: Role.cs
// Description: This is the class for roles such as Admin or User, this will allow Admins permissions like specific settings
// or history pages that not all users need.
// Notes: Currently it is not set up to have an impact on our code
// ***************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
   
    public enum Permission
    {
        Settings,
        History,
        UserSettings,
        CreateRole,
        CreateUser,
        Reports
    }

    public class Role
    {
        public string Name { get; set; }
        public int ID { get; set; }

        public Role(string name, int id)
        {
            Name = name;
            ID = id;
        }
    }
}
