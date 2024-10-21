using GoCardlessToYnabSync.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GoCardlessToYnabSync.Services
{
    public class MailService
    {
        private readonly SmptOptions _smptOptions;
        private readonly GoCardlessOptions _goCardlessOptions;

        public MailService(
            IOptions<SmptOptions> smptOptions,
            IOptions<GoCardlessOptions> goCardlessOptions)
        {
            _smptOptions = smptOptions.Value;
            _goCardlessOptions = goCardlessOptions.Value;
        }

        public void SendAuthMail(string authLink, bool resend = false)
        {
            MailMessage mailMessage = new();
            mailMessage.From = new(_smptOptions.Email);
            mailMessage.To.Add(_smptOptions.SendTo);

            if (!resend)
            {
                mailMessage.Subject = $"GoCardlessToYnabSync: Authenticate the new requisition Id for {_goCardlessOptions.BankId}";
                mailMessage.Body = $"Hello {_smptOptions.Email}, \n\n You're old requisition Id was invalid, use the link below to authenticate the new one:\n {authLink}. \n\n If the Requistion ID is not authenticated before next Sync you will receive a reminder mail to authenticate.";
            }
            else
            {
                mailMessage.Subject = $"GoCardlessToYnabSync: your Requistion ID is still undergoing authentication for {_goCardlessOptions.BankId}";
                mailMessage.Body = $"Hello {_smptOptions.Email}, \n\n Your Requistion ID has not been authenticated yet for the bank {_goCardlessOptions.BankId}, use the link below to authenticate the new one:\n {authLink}\n\n You will receive this mail everytime the Sync is executed and the Requistion ID has not been authenticated.";
            }

            using SmtpClient smtpClient = new();
            smtpClient.Host = _smptOptions.Host;
            smtpClient.Port = _smptOptions.Port;
            smtpClient.UseDefaultCredentials = false;
            smtpClient.Credentials = new NetworkCredential(_smptOptions.Email, _smptOptions.Password);
            smtpClient.EnableSsl = true;

            smtpClient.Send(mailMessage);
        }

        public void SendMail(string fullMessage, string subject)
        {
            MailMessage mailMessage = new();
            mailMessage.From = new(_smptOptions.Email);
            mailMessage.To.Add(_smptOptions.SendTo);
            mailMessage.Subject = $"GoCardlessToYnabSync: {subject}";
            mailMessage.Body = $"Hello {_smptOptions.Email}, \n\n {subject}: {fullMessage}";

            using SmtpClient smtpClient = new();
            smtpClient.Host = _smptOptions.Host;
            smtpClient.Port = _smptOptions.Port;
            smtpClient.UseDefaultCredentials = false;
            smtpClient.Credentials = new NetworkCredential(_smptOptions.Email, _smptOptions.Password);
            smtpClient.EnableSsl = true;

            smtpClient.Send(mailMessage);
        }
    }
}
