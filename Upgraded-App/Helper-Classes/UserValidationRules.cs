// ***************************************************************************************************************************
// File: UserValidationRules.cs
// Description: This is a helper class to ensure all places of account creation follow the same rules and checks. This includes both field validation (e.g. email format, password length) and database checks (e.g. username already exists).
// By centralizing this logic, we can maintain consistency across the app and easily update requirements in one place.
// Notes: N/A
// ***************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Data.SqlClient;

namespace FishLens_App
{
    public static class UserValidationRules
    {
        //  Requirements — change these to update rules app-wide 
        public const int MinUsernameLength = 6;
        public const int MinPasswordLength = 6;

        public const string UsernameHint = $"• At least 6 characters";
        public const string PasswordHint = $"• At least 6 characters";
        public const string PasswordMatchHint = "• Passwords must match";
        public const string EmailHint = "• Must be a valid address (e.g. name@gmail.com)";
        

        // ****************************************************************
        // Function: IsValidEmail
        // Description: Validates an email address using a regular expression.
        // Notes: Uses a conservative regex that allows common characters and TLD lengths (2-10).
        public static bool IsValidEmail(string email)
        {
            string pattern = @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,10}$";
            return Regex.IsMatch(email, pattern);
        }

        // ****************************************************************
        // Function: IsValidUsername
        // Description: Verifies username is non-empty and meets the minimum length requirement.
        // Notes: Uses the MinUsernameLength constant.
        public static bool IsValidUsername(string username) => !string.IsNullOrWhiteSpace(username) && username.Length >= MinUsernameLength;

        // ****************************************************************
        // Function: IsValidPassword
        // Description: Verifies password is non-empty and meets the minimum length requirement.
        // Notes: Uses the MinPasswordLength constant.
        public static bool IsValidPassword(string password) => !string.IsNullOrWhiteSpace(password) && password.Length >= MinPasswordLength;

        // ****************************************************************
        // Function: PasswordsMatch
        // Description: Compares password and confirmation for exact equality.
        // Notes: Simple equality check.
        public static bool PasswordsMatch(string password, string confirm) => password == confirm;

        // ****************************************************************
        // Function: OrgNameExists
        // Description: Checks whether an organization with the given name exists in the database.
        // Notes: Executes a COUNT(*) query and returns true when count > 0.
        public static bool OrgNameExists(SqlConnection conn, string orgName)
        {
            using (SqlCommand cmd = new SqlCommand(
                "SELECT COUNT(*) FROM [kaharra].[Organizations] WHERE Name = @name", conn))
            {
                cmd.Parameters.AddWithValue("@name", orgName);
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
        }

        // ****************************************************************
        // Function: UsernameExists
        // Description: Checks whether a username already exists in the users table.
        // Notes: Optionally excludes a specific user id (useful when editing an existing user).
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

        // ****************************************************************
        // Function: EmailExists
        // Description: Checks whether an email address is already in use by another user.
        // Notes: Optionally excludes a specific user id (useful when editing an existing user).
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

        // ****************************************************************
        // Function: ValidateSignUpFields
        // Description: Validates sign-up form fields (organization, email, username, password, confirm password) without DB access.
        // Notes: Returns a ValidationResult containing field-specific errors.
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

        // ****************************************************************
        // Function: ValidateSignUpDb
        // Description: Performs database duplicate checks for sign-up (org name, username, email).
        // Notes: Intended to be called after field validation succeeds.
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

        // ****************************************************************
        // Function: ValidateCreateUser
        // Description: Validates create-user fields and performs DB duplicate checks when appropriate.
        // Notes: Used by account settings UI; returns field-specific errors.
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

        // ****************************************************************
        // Function: ValidateEditUser
        // Description: Validates a single user's editable fields (email) and checks for duplicates.
        // Notes: Excludes the current user from the duplicate check.
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

        // ****************************************************************
        // Function: ValidatePasswordReset
        // Description: Validates password and confirmation during a reset flow.
        // Notes: Ensures minimum length and that passwords match.
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

        // ****************************************************************
        // Function: IsValid
        // Description: Indicates whether any field errors have been recorded.
        // Notes: True when no errors exist.
        public bool IsValid => _errors.Count == 0;

        // ****************************************************************
        // Function: AddError
        // Description: Adds a field-specific error message if one is not already present.
        // Notes: First error for a field takes precedence.
        public void AddError(string field, string message)
        {
            // First error per field wins 
            if (!_errors.ContainsKey(field))
                _errors[field] = message;
        }

        // ****************************************************************
        // Function: HasErrorFor
        // Description: Returns whether an error exists for the specified field.
        // Notes: Simple dictionary lookup.
        public bool HasErrorFor(string field) => _errors.ContainsKey(field);

        // ****************************************************************
        // Function: GetError
        // Description: Retrieves the error message for the specified field, or null if none.
        // Notes: Uses TryGetValue to avoid exceptions.
        public string GetError(string field)
            => _errors.TryGetValue(field, out var msg) ? msg : null;
    }
}




