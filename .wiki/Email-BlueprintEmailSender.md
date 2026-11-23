# BlueprintEmailSender

`BlueprintEmailSender` is the abstract base class for email senders in the `Blueprint.Email.Senders` namespace. An application can configure a concrete sender once and pass it to methods that accept a `BlueprintEmailSender`. Those methods can then send a [BlueprintEmail](Email-BlueprintEmail.md) without needing the server settings.

## Send a message

Choose one of the two methods for each message.

| Method | When to use it |
| --- | --- |
| `void Send(BlueprintEmail email)` | Use in synchronous code. The call blocks until the transport accepts the message or an error occurs. |
| `Task SendAsync(BlueprintEmail email)` | Use in asynchronous code. Call `await sender.SendAsync(email)` to wait for acceptance and handle any error at the call site. |

For examples of both calls, see [sending a plain-text message](Email-BlueprintEmail.md#send-a-plain-text-message) and [sending a receipt asynchronously](Email-BlueprintEmail.md#copy-recipients-and-attach-a-file).

## Implementations

The built-in SMTP implementation opens a connection for each submission. To configure the host, port, TLS, and optional credentials, see [SMTPEmailSender](Email-SMTPEmailSender.md).

To add another transport, derive from `BlueprintEmailSender` and override both methods. A successful return from `Send`, or successful completion of the task from `SendAsync`, indicates that the transport accepted the message for delivery.
