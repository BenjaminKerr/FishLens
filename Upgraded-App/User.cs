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
        public string Username { get; set; }
        public string Password { get; set; }
        public Role role { get; set; }

        public User(string username, string pass, Role Role)
        {
            Username = username;
            Password = pass;
            Role = role;
        }


        

    }
}
