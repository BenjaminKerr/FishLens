// ***************************************************************************************************************************
// File: User.cs
// Description: This is the class for users; it contains a few properties and constructors used by the app.
// Notes: N/A
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
        public string Email { get; set; }

        // ****************************************************************
        // Function: User(string username, string pass, Role Role, string email)
        // Description: Constructs a new user instance for creation flows (username, password, role, email).
        // Notes: N/A
        public User(string username, string pass, Role Role, string email)
        {
            Username = username;
            Password = pass;
            Email = email;
            Role = role;
        }

        // ****************************************************************
        // Function: User(int id, string username, Role userRole, string email)
        // Description: Constructs a user instance representing an existing user (includes Id, username, role, email).
        // Notes: Used when loading users from storage; password is not required.
        public User(int id, string username, Role userRole, string email)
        {
            Id = id;
            Username = username;
            role = userRole;
            Email = email;
        }
    }
}
