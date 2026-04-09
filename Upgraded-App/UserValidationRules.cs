using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Data.SqlClient;

namespace FishLens_App
{
    // ****************************************************************
    // Class: UserValidationRules
    // Description: Single source of truth for all user input validation.
    //              Update requirements here and they apply everywhere.
    // ****************************************************************
    public static class UserValidationRules
    {
        //  Requirements — change these to update rules app-wide 
        public const int MinUsernameLength = 6;
        public const int MinPasswordLength = 6;

        public const string UsernameHint = $"• At least 6 characters";
        public const string PasswordHint = $"• At least 6 characters";
        public const string PasswordMatchHint = "• Passwords must match";
        public const string EmailHint = "• Must be a valid address (e.g. name@gmail.com)";
        

        public static bool IsValidEmail(string email)
        {
            string pattern = @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,10}$";
            return Regex.IsMatch(email, pattern);
        }

        public static bool IsValidUsername(string username) => !string.IsNullOrWhiteSpace(username) && username.Length >= MinUsernameLength;

        public static bool IsValidPassword(string password) => !string.IsNullOrWhiteSpace(password) && password.Length >= MinPasswordLength;

        public static bool PasswordsMatch(string password, string confirm) => password == confirm;

        public static bool OrgNameExists(SqlConnection conn, string orgName)
        {
            using (SqlCommand cmd = new SqlCommand(
                "SELECT COUNT(*) FROM [kaharra].[Organizations] WHERE Name = @name", conn))
            {
                cmd.Parameters.AddWithValue("@name", orgName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public static bool UsernameExists(SqlConnection conn, string username, int excludeUserId = -1)
        {
            string sql = excludeUserId == -1
                ? "SELECT COUNT(*) FROM [kaharra].[FishLensUsers] WHERE Username = @user"
                : "SELECT COUNT(*) FROM [kaharra].[FishLensUsers] WHERE Username = @user AND Id != @id";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@user", username);
                if (excludeUserId != -1)
                    cmd.Parameters.AddWithValue("@id", excludeUserId);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        public static bool EmailExists(SqlConnection conn, string email, int excludeUserId = -1)
        {
            string sql = excludeUserId == -1
                ? "SELECT COUNT(*) FROM [kaharra].[FishLensUsers] WHERE Email = @email"
                : "SELECT COUNT(*) FROM [kaharra].[FishLensUsers] WHERE Email = @email AND Id != @id";

            using (SqlCommand cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@email", email);
                if (excludeUserId != -1)
                    cmd.Parameters.AddWithValue("@id", excludeUserId);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // ── Full validation bundles ─────────────────────────────────

        // Used by SignUpPage — validates fields only, no DB connection needed
        public static ValidationResult ValidateSignUpFields(
            string orgName, string email, string username,
            string password, string confirmPassword)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(orgName))
                result.AddError("orgName", "Organization name is required.");

            if (string.IsNullOrWhiteSpace(email))
                result.AddError("email", "Email address is required.");
            else if (!IsValidEmail(email))
                result.AddError("email", "Must be a valid address (e.g. name@gmail.com or name@oit.edu).");

            if (string.IsNullOrWhiteSpace(username))
                result.AddError("username", "Username is required.");
            else if (!IsValidUsername(username))
                result.AddError("username", $"Username must be at least {MinUsernameLength} characters.");

            if (string.IsNullOrWhiteSpace(password))
                result.AddError("password", "Password is required.");
            else if (!IsValidPassword(password))
                result.AddError("password", $"Password must be at least {MinPasswordLength} characters.");

            if (!PasswordsMatch(password, confirmPassword))
                result.AddError("confirmPassword", "Passwords do not match.");

            return result;
        }

        // Used by SignUpPage — DB duplicate checks after field validation passes
        public static ValidationResult ValidateSignUpDb(
            SqlConnection conn, string orgName, string email, string username)
        {
            var result = new ValidationResult();

            if (OrgNameExists(conn, orgName))
                result.AddError("orgName", "An organization with that name already exists.");

            if (UsernameExists(conn, username))
                result.AddError("username", "That username is already taken.");

            if (EmailExists(conn, email))
                result.AddError("email", "That email is already in use.");

            return result;
        }

        // Used by Settings create user — fields + DB
        public static ValidationResult ValidateCreateUser(
            SqlConnection conn, string username, string email, string password)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(username))
                result.AddError("username", "Username is required.");
            else if (!IsValidUsername(username))
                result.AddError("username", $"Username must be at least {MinUsernameLength} characters.");

            if (string.IsNullOrWhiteSpace(email))
                result.AddError("email", "Email address is required.");
            else if (!IsValidEmail(email))
                result.AddError("email", "Must be a valid address (e.g. name@gmail.com or name@oit.edu).");

            if (string.IsNullOrWhiteSpace(password))
                result.AddError("password", "Password is required.");
            else if (!IsValidPassword(password))
                result.AddError("password", $"Password must be at least {MinPasswordLength} characters.");

            // DB checks only if field checks passed for those fields
            if (!result.HasErrorFor("username") && UsernameExists(conn, username))
                result.AddError("username", "That username is already taken.");

            if (!result.HasErrorFor("email") && EmailExists(conn, email))
                result.AddError("email", "That email is already in use.");

            return result;
        }

        // Used by Settings save user — validates a single user being edited
        public static ValidationResult ValidateEditUser(SqlConnection conn, int userId, string username, string email)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(email))
                result.AddError("email", "Email is required.");
            else if (!IsValidEmail(email))
                result.AddError("email", "Email is not valid.");

            if (!result.HasErrorFor("email") && EmailExists(conn, email, excludeUserId: userId))
                result.AddError("email", "That email is already in use by another account.");

            return result;
        }

        // Used by ForgotPasswordWindow reset step
        public static ValidationResult ValidatePasswordReset(string password, string confirmPassword)
        {
            var result = new ValidationResult();

            if (string.IsNullOrWhiteSpace(password))
                result.AddError("password", "Password is required.");
            else if (!IsValidPassword(password))
                result.AddError("password", $"Password must be at least {MinPasswordLength} characters.");

            if (!PasswordsMatch(password, confirmPassword))
                result.AddError("confirmPassword", "Passwords do not match.");

            return result;
        }
    }

    // ****************************************************************
    // Class: ValidationResult
    // Description: Holds field-keyed errors so each page can route
    //              error messages to the right inline TextBlock
    // ****************************************************************
    public class ValidationResult
    {
        private readonly Dictionary<string, string> _errors = new();

        public bool IsValid => _errors.Count == 0;

        public void AddError(string field, string message)
        {
            // First error per field wins 
            if (!_errors.ContainsKey(field))
                _errors[field] = message;
        }

        public bool HasErrorFor(string field) => _errors.ContainsKey(field);

        public string GetError(string field)
            => _errors.TryGetValue(field, out var msg) ? msg : null;
    }
}




