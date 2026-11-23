# SMTPEmailSender

`SMTPEmailSender` implements the synchronous `Send` and asynchronous `SendAsync` methods of `BlueprintEmailSender`. Each call opens an SMTP connection and completes when the server accepts the message. Connection, authentication, and submission errors propagate to the caller.

## Send a message

This example takes the SMTP credentials and attachment bytes from the application.

```csharp
using Blueprint.Email;
using Blueprint.Email.Senders;
using Blueprint.Email.Senders.SMTP;
using System.Net;
using System.Threading.Tasks;

static async Task SendInvoiceAsync(string host, NetworkCredential credentials, string from, string to, byte[] pdf)
{
    SMTPSettings settings = new SMTPSettings(host) { Credentials = credentials };
    BlueprintEmailSender sender = new SMTPEmailSender(settings);
    BlueprintEmail email = new BlueprintEmail(from)
    {
        Subject = "Your invoice",
        Body = "Your invoice is attached."
    };
    email.To.Add(to);
    email.Attachments.Add(new BlueprintEmailAttachment("invoice.pdf", pdf, "application/pdf"));
    await sender.SendAsync(email);
}
```

For synchronous submission, call `sender.Send(email)`. For examples of plain text, HTML, copy recipients, and attachments, see [BlueprintEmail](Email-BlueprintEmail.md).

## Connection settings

The default port is 587. Port 465 uses TLS from the start of the connection. All other ports require STARTTLS by default. Use `SMTPSettings.SocketOptions` to select the mode specified by the server, including `MailKit.Security.SecureSocketOptions.None` for an unencrypted local test relay. Omit `Credentials` when the relay accepts anonymous submission.
