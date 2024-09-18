using GoCardlessToYnabSync.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace GoCardlessToYnabSync.Services
{
    public class MailService
    {
        private readonly IConfiguration _configuration;

        public MailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void SendAuthMail(string authLink, string bankId, bool resend = false)
        {
            var smptOptions = new SmptOptions();
            _configuration.GetSection(SmptOptions.Smpt).Bind(smptOptions);

            MailMessage mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(smptOptions.Email);
            mailMessage.To.Add(smptOptions.SendTo);

            if (!resend)
            {
                mailMessage.Subject = $"GoCardlessToYnabSync: Authenticate the new requisition Id for {bankId}";
                mailMessage.Body = $"Hello {smptOptions.Email}, \n\n You're old requisition Id was invalid, use the link below to authenticate the new one:\n {authLink}. \n\n If the Requistion ID is not authenticated before next Sync you will receive a reminder mail to authenticate.";
            }
            else
            {
                mailMessage.Subject = $"GoCardlessToYnabSync: your Requistion ID is still undergoing authentication for {bankId}";
                mailMessage.Body = $"Hello {smptOptions.Email}, \n\n Your Requistion ID has not been authenticated yet for the bank {bankId}, use the link below to authenticate the new one:\n {authLink}\n\n You will receive this mail everytime the Sync is executed and the Requistion ID has not been authenticated.";
            }

            SmtpClient smtpClient = new SmtpClient();
            smtpClient.Host = smptOptions.Host;
            smtpClient.Port = smptOptions.Port;
            smtpClient.UseDefaultCredentials = false;
            smtpClient.Credentials = new NetworkCredential(smptOptions.Email, smptOptions.Password);
            smtpClient.EnableSsl = true;

            smtpClient.Send(mailMessage);
        }

        public void SendMail(string fullMessage, string subject)
        {
            var smptOptions = new SmptOptions();
            _configuration.GetSection(SmptOptions.Smpt).Bind(smptOptions);

            MailMessage mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(smptOptions.Email);
            mailMessage.To.Add(smptOptions.SendTo);
            mailMessage.Subject = $"GoCardlessToYnabSync: {subject}";
            mailMessage.Body = $"Hello {smptOptions.Email}, \n\n {subject}: {fullMessage}";

            SmtpClient smtpClient = new SmtpClient();
            smtpClient.Host = smptOptions.Host;
            smtpClient.Port = smptOptions.Port;
            smtpClient.UseDefaultCredentials = false;
            smtpClient.Credentials = new NetworkCredential(smptOptions.Email, smptOptions.Password);
            smtpClient.EnableSsl = true;

            smtpClient.Send(mailMessage);
        }
    }
}
