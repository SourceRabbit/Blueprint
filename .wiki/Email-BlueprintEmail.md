# BlueprintEmail

`BlueprintEmail` holds the sender address, recipients, subject, body, and attachments of one outgoing message. Pass it to a `BlueprintEmailSender` to send it. Application methods can accept the abstract sender as a parameter, as in the examples below. To create a sender for an SMTP server, see [SMTPEmailSender](Email-SMTPEmailSender.md).

## Send a plain-text message

Pass the sender address to the `BlueprintEmail` constructor. It accepts either an email address or a display name followed by an address in angle brackets. Add the recipient to `To` and call `Send` to wait for the transport to accept the message.

```csharp
using Blueprint.Email;
using Blueprint.Email.Senders;

static void SendOrderConfirmation(BlueprintEmailSender sender, string customerEmail)
{
    BlueprintEmail email = new BlueprintEmail("Orders <orders@example.com>")
    {
        Subject = "Order confirmed",
        Body = "We received your order."
    };
    email.To.Add(customerEmail);
    sender.Send(email);
}
```

## Copy recipients and attach a file

Use `CC` for recipients whose addresses should be visible and `Bcc` for recipients whose addresses should stay hidden from the others. For SMTP, at least one address must appear in `To`, `CC`, or `Bcc`.

The body is plain text by default. This example sets `IsBodyHtml`, attaches a PDF, and uses `await sender.SendAsync(email)` so the calling thread can do other work while the transport accepts the message.

```csharp
using Blueprint.Email;
using Blueprint.Email.Senders;
using System.Threading.Tasks;

static async Task SendReceiptAsync(BlueprintEmailSender sender, string customerEmail, byte[] receiptPdf)
{
    BlueprintEmail email = new BlueprintEmail("Billing <billing@example.com>")
    {
        Subject = "Your receipt",
        Body = "<p>Thank you for your payment. Your receipt is attached.</p>",
        IsBodyHtml = true
    };
    email.To.Add(customerEmail);
    email.CC.Add("accounts@example.com");
    email.Bcc.Add("archive@example.com");
    email.Attachments.Add(new BlueprintEmailAttachment("receipt.pdf", receiptPdf, "application/pdf"));
    await sender.SendAsync(email);
}
```

## BlueprintEmailAttachment

`BlueprintEmailAttachment` copies the supplied bytes and uses the file name you provide as the attachment name. The MIME type defaults to `application/octet-stream` when omitted. Add one attachment to `email.Attachments` for each file.
