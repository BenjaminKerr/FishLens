// ***************************************************************************************************************************
// File: EmailService.cs
// Description: Creates an email to send to the user either confirming email or sending a reset code for password reset. This is used in the forgot password and email verification processes.
// Notes: N/A
// ***************************************************************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MimeKit;


namespace FishLens_App
{
    public class EmailService
    {
        private string smtpServer = "smtp.gmail.com";
        private int smtpPort = 587;
        private string fromEmail = "fishlensapp.help@gmail.com";
        // app password generated inside account settings
        private string fromPassword = "zpfl myjb lbhi cece";


        // ****************************************************************
        // Function: SendResetCode
        // Description: Composes and sends a plaintext password reset email containing the provided reset code.
        // Notes: Uses MailKit to connect to the SMTP server; returns true on success and false on exception.
        public bool SendResetCode(string toEmail, string resetCode)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("FishLens", fromEmail));
                message.To.Add(new MailboxAddress("", toEmail));
                message.Subject = "FishLens Password Reset";

                message.Body = new TextPart("plain")
                {
                    Text = $"Your password reset code is: {resetCode}\n\n" +
                           $"This code expires in 15 minutes.\n\n" +
                           $"If you did not request this, please ignore this email."
                };

                using (var client = new SmtpClient())
                {
                    client.Connect(smtpServer, smtpPort, false);
                    client.Authenticate(fromEmail, fromPassword);
                    client.Send(message);
                    client.Disconnect(true);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Email error: " + ex.Message);
                return false;
            }
        }

        // ****************************************************************
        // Function: SendVerificationCode
        // Description: Composes and sends an account verification email containing the provided verification code.
        // Notes: Uses MailKit to connect to the SMTP server; returns true on success and false on exception.
        public bool SendVerificationCode(string toEmail, string resetCode)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress("FishLens", fromEmail));
                message.To.Add(new MailboxAddress("", toEmail));
                message.Subject = "FishLens Email Verification";

                message.Body = new TextPart("plain")
                {
                    Text = $"Welcome to FishLens!\n\n" +
                    $"Your email verification code is: {resetCode}\n\n" +
                           $"This code expires in 15 minutes.\n\n" +
                           $"If you did not create a FishLens account, please ignore this email."
                };

                using (var client = new SmtpClient())
                {
                    client.Connect(smtpServer, smtpPort, false);
                    client.Authenticate(fromEmail, fromPassword);
                    client.Send(message);
                    client.Disconnect(true);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Email error: " + ex.Message);
                return false;
            }
        }
    }

}
