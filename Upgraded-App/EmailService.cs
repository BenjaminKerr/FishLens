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
