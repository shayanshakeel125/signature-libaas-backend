using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace backend.Services
{
    /// <summary>
    /// Sends the order confirmation email to the customer and the new-order
    /// notification email to the store owner over Gmail SMTP (MailKit/MimeKit).
    /// </summary>
    public class EmailService
    {
        private const string DefaultSmtpServer = "smtp.gmail.com";
        private const int DefaultPort = 587;
        private const string DefaultSenderName = "Signature Libaas";
        private const string DefaultAdminEmail = "shayanshakeel832@gmail.com";

        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Sends both order emails. Throws only when the SMTP connection or login
        /// fails; a single failed message is logged and does not stop the other one.
        /// </summary>
        public async Task SendOrderEmailsAsync(
            string customerName,
            string customerEmail,
            string phone,
            string address,
            string city,
            decimal totalAmount,
            int orderId = 0)
        {
            var settings = ReadSettings();

            var messages = new List<MimeMessage>();

            if (!string.IsNullOrWhiteSpace(customerEmail))
            {
                messages.Add(BuildCustomerMessage(settings, customerName, customerEmail, phone, address, city, totalAmount, orderId));
            }
            else
            {
                _logger.LogWarning("Order {OrderId}: customer email is empty, confirmation email skipped.", orderId);
            }

            messages.Add(BuildAdminMessage(settings, customerName, customerEmail, phone, address, city, totalAmount, orderId));

            using var client = new SmtpClient();

            try
            {
                await client.ConnectAsync(settings.SmtpServer, settings.Port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(settings.SenderEmail, settings.Password);

                foreach (var message in messages)
                {
                    var recipient = message.To.Mailboxes.FirstOrDefault()?.Address ?? "(unknown)";

                    try
                    {
                        await client.SendAsync(message);
                        _logger.LogInformation("Sent '{Subject}' to {Recipient}.", message.Subject, recipient);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send '{Subject}' to {Recipient}.", message.Subject, recipient);
                    }
                }
            }
            finally
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true);
                }
            }
        }

        // ---------------------------------------------------------------- messages

        private static MimeMessage BuildCustomerMessage(
            EmailSettings settings,
            string customerName,
            string customerEmail,
            string phone,
            string address,
            string city,
            decimal totalAmount,
            int orderId)
        {
            var name = string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
            message.To.Add(new MailboxAddress(name, customerEmail));
            message.Subject = orderId > 0
                ? $"Order Confirmation #{orderId} - Signature Libaas"
                : "Order Confirmation - Signature Libaas";
            message.ReplyTo.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));

            var orderLine = orderId > 0
                ? $"<p><strong>Order ID:</strong> #{orderId}</p>"
                : string.Empty;

            var body = new BodyBuilder
            {
                HtmlBody = $@"
                    <div style='font-family: Arial, sans-serif; color: #333;'>
                        <h2>Hello {WebUtility.HtmlEncode(name)},</h2>
                        <p>Thank you for shopping at <strong>Signature Libaas</strong>! Your order has been placed successfully.</p>
                        <h3>Order Summary:</h3>
                        {orderLine}
                        <p><strong>Total Amount:</strong> Rs. {totalAmount:N0}</p>
                        <p><strong>Shipping Address:</strong> {WebUtility.HtmlEncode(address)}, {WebUtility.HtmlEncode(city)}</p>
                        <p>We will contact you at {WebUtility.HtmlEncode(phone)} before delivery.</p>
                        <br/>
                        <p>Best Regards,<br/><strong>Signature Libaas Team</strong></p>
                    </div>"
            };

            message.Body = body.ToMessageBody();
            return message;
        }

        private static MimeMessage BuildAdminMessage(
            EmailSettings settings,
            string customerName,
            string customerEmail,
            string phone,
            string address,
            string city,
            decimal totalAmount,
            int orderId)
        {
            var name = string.IsNullOrWhiteSpace(customerName) ? "(not provided)" : customerName;

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderEmail));
            message.To.Add(new MailboxAddress("Signature Libaas Admin", settings.AdminEmail));
            message.Subject = orderId > 0
                ? $"NEW ORDER #{orderId} - Rs. {totalAmount:N0}"
                : $"NEW ORDER RECEIVED - Rs. {totalAmount:N0}";

            var orderLine = orderId > 0
                ? $"<p><strong>Order ID:</strong> #{orderId}</p>"
                : string.Empty;

            var body = new BodyBuilder
            {
                HtmlBody = $@"
                    <div style='font-family: Arial, sans-serif; color: #333;'>
                        <h2 style='color: #27ae60;'>New Order Placed!</h2>
                        {orderLine}
                        <p><strong>Customer Name:</strong> {WebUtility.HtmlEncode(name)}</p>
                        <p><strong>Customer Email:</strong> {WebUtility.HtmlEncode(customerEmail)}</p>
                        <p><strong>Phone:</strong> {WebUtility.HtmlEncode(phone)}</p>
                        <p><strong>Address:</strong> {WebUtility.HtmlEncode(address)}, {WebUtility.HtmlEncode(city)}</p>
                        <p><strong>Total Amount:</strong> Rs. {totalAmount:N0}</p>
                    </div>"
            };

            message.Body = body.ToMessageBody();
            return message;
        }

        // ------------------------------------------------------------------ config

        private EmailSettings ReadSettings()
        {
            var section = _configuration.GetSection("EmailSettings");

            var smtpServer = section["SmtpServer"];
            if (string.IsNullOrWhiteSpace(smtpServer))
            {
                smtpServer = DefaultSmtpServer;
            }

            var senderName = section["SenderName"];
            if (string.IsNullOrWhiteSpace(senderName))
            {
                senderName = DefaultSenderName;
            }

            var senderEmail = section["SenderEmail"];
            if (string.IsNullOrWhiteSpace(senderEmail))
            {
                throw new InvalidOperationException("EmailSettings:SenderEmail is missing from configuration.");
            }

            var password = section["Password"];
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException("EmailSettings:Password is missing from configuration.");
            }

            var adminEmail = section["AdminEmail"];
            if (string.IsNullOrWhiteSpace(adminEmail))
            {
                adminEmail = DefaultAdminEmail;
            }

            var port = section.GetValue<int?>("Port") ?? DefaultPort;
            if (port is <= 0 or > 65535)
            {
                port = DefaultPort;
            }

            return new EmailSettings
            {
                SmtpServer = smtpServer,
                Port = port,
                SenderName = senderName,
                SenderEmail = senderEmail,
                Password = password,
                AdminEmail = adminEmail
            };
        }

        private sealed class EmailSettings
        {
            public required string SmtpServer { get; init; }
            public required int Port { get; init; }
            public required string SenderName { get; init; }
            public required string SenderEmail { get; init; }
            public required string Password { get; init; }
            public required string AdminEmail { get; init; }
        }
    }
}
