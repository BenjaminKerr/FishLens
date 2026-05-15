// ***************************************************************************************************************************
// File: AccountSettings.xaml.cs
// Description: This is the code behind for the account settings page, this will allow users to create new accounts and edit existing accounts. It will also allow admins to manage user roles and permissions.
// Notes: N/A
// ***************************************************************************************************************************

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



        // ****************************************************************
        // Function: AccountSettings
        // Description: Initializes the page, sets data context and loads roles and users.
        // Notes: Called by WPF during construction.
        public AccountSettings()
        {
            InitializeComponent();

            DataContext = this;
            LoadRoles();
            LoadUsers();
        }



        // ****************************************************************
        // Function: ScrollViewer_PreviewMouseWheel
        // Description: Slows and smooths scroll speed for the scroll viewer.
        // Notes: Reduces mouse wheel delta before applying vertical offset.
        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - (e.Delta * 0.25));
                e.Handled = true;
            }
        }

        // ****************************************************************
        // Function: CreateUserClick
        // Description: Handles the Create User button — validates input, attempts signup and updates UI.
        // Notes: Validates via `UserValidationRules`, shows inline errors or success and reloads users on success.
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

        // ****************************************************************
        // Function: DeleteUser_Click
        // Description: Handler for the Delete button on each user row.
        // Notes: Confirms via username retype, then calls DeleteUserInDb. Refreshes the
        //        list on success or shows an error message inline on failure.
        private void DeleteUser_Click(object sender, RoutedEventArgs e)
        {
            SaveUsersError.Visibility = Visibility.Collapsed;
            SaveUsersSuccess.Visibility = Visibility.Collapsed;

            if (sender is not Button btn || btn.DataContext is not User user)
                return;

            var app = Application.Current as App;
            if (app == null)
            {
                SaveUsersError.Text = "Not signed in.";
                SaveUsersError.Visibility = Visibility.Visible;
                return;
            }

            // Defense in depth — UI hides the button, but double-check here.
            if (user.Id == app.CurrentUserId)
            {
                SaveUsersError.Text = "You cannot delete your own account.";
                SaveUsersError.Visibility = Visibility.Visible;
                return;
            }

            if (!ConfirmDeleteUser(user.Username))
                return;  // user cancelled

            if (DeleteUserInDb(user.Id, out string error))
            {
                SaveUsersSuccess.Text = $"Successfully deleted '{user.Username}'.";
                SaveUsersSuccess.Visibility = Visibility.Visible;
                LoadUsers();  // refresh the list
            }
            else
            {
                SaveUsersError.Text = $"Could not delete '{user.Username}': {error}";
                SaveUsersError.Visibility = Visibility.Visible;
            }
        }




        // ****************************************************************
        // Function: SaveUserChanges_Click
        // Description: Persists edited users to the database after validation.
        // Notes: Detects changed rows, validates them, updates DB and reports success or errors inline.
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

        // ****************************************************************
        // Function: RefreshUsers_Click
        // Description: Reloads the users list.
        // Notes: Simple wrapper that calls `LoadUsers`.
        private void RefreshUsers_Click(object sender, RoutedEventArgs e) => LoadUsers();

        // ****************************************************************
        // Function: UsersGrid_SelectionChanged
        // Description: Handles selection changes in the users grid.
        // Notes: Currently a placeholder for potential future behavior.
        private void UsersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) { }


        // ****************************************************************
        // Function: ClearCreateUserErrors
        // Description: Clears all inline create-user and save-user error/success messages.
        // Notes: Resets visibility of related TextBlocks.
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

        // ****************************************************************
        // Function: ShowCreateUserError
        // Description: Displays a message in the provided TextBlock.
        // Notes: Sets text and visibility.
        private void ShowCreateUserError(TextBlock block, string message)
        {
            block.Text = message;
            block.Visibility = Visibility.Visible;
        }

        // ****************************************************************
        // Function: ApplyCreateUserErrors
        // Description: Routes field-specific validation errors to their corresponding UI elements.
        // Notes: Uses `ValidationResult` to determine which messages to show.
        private void ApplyCreateUserErrors(ValidationResult result)
        {
            if (result.HasErrorFor("username"))
                ShowCreateUserError(CreateUserUsernameError, result.GetError("username"));
            if (result.HasErrorFor("email"))
                ShowCreateUserError(CreateUserEmailError, result.GetError("email"));
            if (result.HasErrorFor("password"))
                ShowCreateUserError(CreateUserPasswordError, result.GetError("password"));
        }

        // ****************************************************************
        // Function: SignUp
        // Description: Inserts a new user into the database using a stored procedure.
        // Notes: Returns true on success, false on exceptions.
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

        private bool DeleteUserInDb(int targetUserId, out string errorMessage)
        {
            errorMessage = null;
            var app = Application.Current as App;
            if (app == null || app.CurrentUserId <= 0)
            {
                errorMessage = "Not signed in.";
                return false;
            }

            try
            {
                using var conn = new System.Data.SqlClient.SqlConnection(app.connectionString);
                conn.Open();
                using var cmd = new System.Data.SqlClient.SqlCommand("kaharra.DeleteUser", conn);
                cmd.CommandType = System.Data.CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@pUserId", targetUserId);
                cmd.Parameters.AddWithValue("@pRequestingUserId", app.CurrentUserId);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (System.Data.SqlClient.SqlException ex)
            {
                // Our proc's RAISERROR messages land here as readable strings
                errorMessage = ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Unexpected error: {ex.Message}";
                return false;
            }
        }


        private bool ConfirmDeleteUser(string username)
        {
            var dlg = new Window
            {
                Title = "Confirm Delete",
                Width = 420,
                Height = 240,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                Background = (System.Windows.Media.Brush)Application.Current.Resources["WindowBackground"]
            };

            var panel = new StackPanel { Margin = new Thickness(24) };

            panel.Children.Add(new TextBlock
            {
                Text = $"Delete user '{username}'?",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"],
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new TextBlock
            {
                Text = "This action cannot be undone. The user's account and settings will be permanently removed.",
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["SecondaryText"],
                Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"Type '{username}' to confirm:",
                FontSize = 12,
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"],
                Margin = new Thickness(0, 0, 0, 6)
            });

            var confirmBox = new TextBox
            {
                FontSize = 14,
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 0, 0, 16),
                Background = (System.Windows.Media.Brush)Application.Current.Resources["CardBackground"],
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"],
                BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"]
            };
            panel.Children.Add(confirmBox);

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Width = 90,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                IsCancel = true,
                Background = (System.Windows.Media.Brush)Application.Current.Resources["CardBackground"],
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["PrimaryText"],
                BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"]
            };

            var deleteBtn = new Button
            {
                Content = "Delete User",
                Width = 110,
                Height = 32,
                IsEnabled = false,
                Background = System.Windows.Media.Brushes.IndianRed,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold
            };

            confirmBox.TextChanged += (s, e) =>
            {
                deleteBtn.IsEnabled = string.Equals(confirmBox.Text, username, StringComparison.Ordinal);
            };

            bool confirmed = false;
            deleteBtn.Click += (s, e) => { confirmed = true; dlg.DialogResult = true; };
            cancelBtn.Click += (s, e) => { dlg.DialogResult = false; };

            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(deleteBtn);
            panel.Children.Add(btnRow);

            dlg.Content = panel;
            dlg.Loaded += (s, e) => confirmBox.Focus();
            dlg.ShowDialog();

            return confirmed;
        }




        // ****************************************************************
        // Function: LoadUsers
        // Description: Reads users for the current organization from the database and binds them to the grid.
        // Notes: Populates `_users`, `_originalUserData` and `UsersGrid.ItemsSource`.
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

        // ****************************************************************
        // Function: LoadRoles
        // Description: Loads role records from the database and populates the role selector.
        // Notes: Sets `Roles`, `RoleComboBox.ItemsSource` and updates DataContext.
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

        // ****************************************************************
        // Function: UpdateUser
        // Description: Updates an existing user's username, role and email in the database.
        // Notes: Returns true on success, false on exceptions.
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

