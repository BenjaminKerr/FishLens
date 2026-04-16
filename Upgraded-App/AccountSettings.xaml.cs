using FishLens_App.Interfaces;
using FishLens_App.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FishLens_App
{
    public partial class AccountSettings : System.Windows.Controls.Page
    {

        private Dictionary<int, (string Username, int RoleId, string email)> _originalUserData = new();

        public List<Role> Roles { get; set; } = new List<Role>();
        private List<User> _users = new List<User>();
        private int CurrentOrgId => (Application.Current as App).CurrentOrganizationId;


    

        public AccountSettings()
        {
            InitializeComponent();

            DataContext = this;
            LoadRoles();
            LoadUsers();
        }



        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.25));
                e.Handled = true;
            }
        }

        public void CreateUserClick(object sender, RoutedEventArgs e)
        {
            ClearCreateUserErrors();

            if (RoleComboBox.SelectedItem is not Role selectedRole)
            {
                ShowCreateUserError(CreateUserRoleError, "Please select a role.");
                return;
            }

            string username = NewUsername.Text.Trim();
            string password = NewPassword.Password;
            string email = NewUserEmail.Text.Trim();

            try
            {
                using (SqlConnection conn = new SqlConnection((Application.Current as App).connectionString))
                {
                    conn.Open();
                    var result = UserValidationRules.ValidateCreateUser(conn, username, email, password);

                    if (!result.IsValid)
                    {
                        ApplyCreateUserErrors(result);
                        return;
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL Exception: " + ex.Message);
                ShowCreateUserError(CreateUserUsernameError, "Something went wrong. Please try again.");
                return;
            }

            if (SignUp(username, email, password, selectedRole.ID))
            {
                NewUsername.Text = "";
                NewPassword.Password = "";
                RoleComboBox.SelectedIndex = -1;
                NewUserEmail.Text = "";
                CreateUserSuccessMessage.Visibility = Visibility.Visible;
                LoadUsers();
            }
            else
            {
                ShowCreateUserError(CreateUserUsernameError, "Sign up unsuccessful, please retry.");
            }
        }

        private void SaveUserChanges_Click(object sender, RoutedEventArgs e)
        {
            SaveUsersError.Visibility = Visibility.Collapsed;
            SaveUsersSuccess.Visibility = Visibility.Collapsed;

            if (_users == null || _users.Count == 0)
            {
                SaveUsersError.Text = "No users to save.";
                SaveUsersError.Visibility = Visibility.Visible;
                return;
            }

            var changedUsers = _users.Where(u =>
            {
                if (string.IsNullOrWhiteSpace(u.Username) || u.role == null) return false;
                if (!_originalUserData.TryGetValue(u.Id, out var original)) return false;
                return u.Username.Trim() != original.Username
                    || u.role.ID != original.RoleId
                    || u.Email?.Trim() != original.email;
            }).ToList();

            if (changedUsers.Count == 0)
            {
                SaveUsersError.Text = "No changes detected.";
                SaveUsersError.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                using (SqlConnection conn = new SqlConnection((Application.Current as App).connectionString))
                {
                    conn.Open();
                    foreach (var u in changedUsers)
                    {
                        var result = UserValidationRules.ValidateEditUser(
                            conn, u.Id, u.Username.Trim(), u.Email?.Trim() ?? "");

                        if (!result.IsValid)
                        {
                            SaveUsersError.Text = $"{u.Username}: {result.GetError("email")}";
                            SaveUsersError.Visibility = Visibility.Visible;
                            return;
                        }
                    }
                }
            }
            catch (SqlException ex)
            {
                Debug.WriteLine("SQL Exception: " + ex.Message);
                SaveUsersError.Text = "Something went wrong. Please try again.";
                SaveUsersError.Visibility = Visibility.Visible;
                return;
            }

            int updated = 0;
            var updatedNames = new List<string>();

            foreach (var u in changedUsers)
            {
                if (UpdateUser(u.Id, u.Username.Trim(), u.role.ID, u.Email.Trim()))
                {
                    updated++;
                    updatedNames.Add(u.Username.Trim());
                }
            }

            if (updated > 0)
            {
                string names = string.Join(", ", updatedNames);
                SaveUsersSuccess.Text = updated == 1
                    ? $"Successfully updated {names}."
                    : $"Successfully updated {updated} users: {names}.";
                SaveUsersSuccess.Visibility = Visibility.Visible;
                LoadUsers();
            }
            else
            {
                SaveUsersError.Text = "Something went wrong — no changes were saved.";
                SaveUsersError.Visibility = Visibility.Visible;
            }
        }

        private void RefreshUsers_Click(object sender, RoutedEventArgs e) => LoadUsers();

        private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }


        private void ClearCreateUserErrors()
        {
            CreateUserRoleError.Visibility = Visibility.Collapsed;
            CreateUserUsernameError.Visibility = Visibility.Collapsed;
            CreateUserEmailError.Visibility = Visibility.Collapsed;
            CreateUserPasswordError.Visibility = Visibility.Collapsed;
            CreateUserSuccessMessage.Visibility = Visibility.Collapsed;
            SaveUsersError.Visibility = Visibility.Collapsed;
            SaveUsersSuccess.Visibility = Visibility.Collapsed;
        }

        private void ShowCreateUserError(TextBlock block, string message)
        {
            block.Text = message;
            block.Visibility = Visibility.Visible;
        }

        private void ApplyCreateUserErrors(ValidationResult result)
        {
            if (result.HasErrorFor("username"))
                ShowCreateUserError(CreateUserUsernameError, result.GetError("username"));
            if (result.HasErrorFor("email"))
                ShowCreateUserError(CreateUserEmailError, result.GetError("email"));
            if (result.HasErrorFor("password"))
                ShowCreateUserError(CreateUserPasswordError, result.GetError("password"));
        }

        private bool SignUp(string username, string email, string password, int roleId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection((Application.Current as App).connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("kaharra.AddUser", conn))
                    {
                        cmd.CommandType = System.Data.CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@pUser", username);
                        cmd.Parameters.AddWithValue("@pPassword", password);
                        cmd.Parameters.AddWithValue("@pRoleId", roleId);
                        cmd.Parameters.AddWithValue("@pOrgId", CurrentOrgId);
                        cmd.Parameters.AddWithValue("@pEmail", email);
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SignUp Exception: " + ex.Message);
                return false;
            }
        }

        private void LoadUsers()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection((Application.Current as App).connectionString))
                {
                    conn.Open();

                    string sql = @"
                        SELECT Id, Username, Email, RoleId
                        FROM [kaharra].[kaharra].[FishLensUsers]
                        WHERE OrganizationId = @orgId
                        ORDER BY Username";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@orgId", CurrentOrgId);

                        using (SqlDataReader reader = cmd.ExecuteReader())
                        {
                            var users = new List<User>();
                            while (reader.Read())
                            {
                                int id = reader.GetInt32(0);
                                string username = reader.GetString(1);
                                string email = reader.GetString(2);
                                int roleId = reader.GetInt32(3);

                                Role matchedRole = Roles.FirstOrDefault(r => r.ID == roleId);
                                users.Add(new User(id, username, matchedRole, email));
                            }

                            _users = users;
                            _originalUserData = users.ToDictionary(
                                u => u.Id,
                                u => (u.Username, u.role?.ID ?? -1, u.Email)
                            );
                            UsersGrid.ItemsSource = _users;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                
                MessageBox.Show("Failed to load users.");
            }
        }

        private void LoadRoles()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection((Application.Current as App).connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT Id, Name FROM Roles", conn))
                    using (SqlDataReader reader = cmd.ExecuteReader())
                    {
                        var roles = new List<Role>();
                        while (reader.Read())
                        {
                            roles.Add(new Role(reader.GetString(1), reader.GetInt32(0)));
                        }
                        Roles = roles;
                        RoleComboBox.ItemsSource = roles;
                        DataContext = this;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("LoadRoles error: " + ex.Message);
                MessageBox.Show("Failed to load roles.");
            }
        }

        private bool UpdateUser(int userId, string newUsername, int newRoleId, string email)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection((Application.Current as App).connectionString))
                {
                    conn.Open();
                    string sql = @"
                        UPDATE [kaharra].[kaharra].[FishLensUsers]
                        SET Username = @user, RoleId = @roleid, Email = @email
                        WHERE Id = @userid";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@user", newUsername);
                        cmd.Parameters.AddWithValue("@roleid", newRoleId);
                        cmd.Parameters.AddWithValue("@email", email);
                        cmd.Parameters.AddWithValue("@userid", userId);
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("UpdateUser error: " + ex.Message);
                return false;
            }
        }

    }
}

