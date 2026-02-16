// ***************************************************************************************************************************
// File: User.cs
// Description: This is the class for users temporalily it only has a few properties but will be adjusted after usage when we 
// determine what is needed per user.
// Notes: Currently it is not set up to have an impact on our code
// ***************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.DirectoryServices.ActiveDirectory;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FishLens_App
{
    class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public Role role { get; set; }

        public User(string username, string pass, Role Role)
        {
            Username = username;
            Password = pass;
            Role = role;
        }

        //Pulling for edit users not sign in so password not needed
        public User(int id, string username, Role userRole)
        {
            Id = id;
            Username = username;
            role = userRole;
        }


    }
}
