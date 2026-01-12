using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

// Created by: Kaharra Wilcoxon
// Created on: 1/11/2025
// Class Description:
// Role is the class of all roles like Admin or User for our Users to allow Admin access to their Employees and other settings/history that not all users/employees need access to

namespace FishLens_App
{
    public enum Permission
    {
        Settings,
        History,
        UserSettings,
        CreateRole,
        CreateUser
    }

    class Role
    {
        public string Name { get; set; }
        public List<Permission> Permissions { get; set; }

        public Role(string name, List<Permission> permissions)
        {
            Name = name;
            Permissions = permissions;
        }
    }
}
