using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web.Script.Serialization;

namespace tcpTrigger
{
    // Sends email through Microsoft 365 with the Microsoft Graph sendMail API. Signs in as an app
    // registration with a client secret. The app needs the Mail.Send application permission.
    internal static class Microsoft365Sender
    {
        // How long to wait for each request.
        private const int TimeoutMilliseconds = 60000;

        // Sends the message from the mailbox in its From address. Only the message's To recipients are used.
        // Throws an exception if signing in or sending fails.
        public static void Send(string tenantId, string clientId, string clientSecret, MailMessage message)
        {
            string token = GetAccessToken(tenantId, clientId, clientSecret);
            string url = "https://graph.microsoft.com/v1.0/users/" + Uri.EscapeDataString(message.From.Address) + "/sendMail";
            Post(url, "application/json", BuildRequest(message), token);
        }

        // Signs in as the app and returns an access token for Microsoft Graph.
        private static string GetAccessToken(string tenantId, string clientId, string clientSecret)
        {
            string url = "https://login.microsoftonline.com/" + Uri.EscapeDataString(tenantId) + "/oauth2/v2.0/token";
            string form = "grant_type=client_credentials"
                + "&client_id=" + Uri.EscapeDataString(clientId)
                + "&client_secret=" + Uri.EscapeDataString(clientSecret)
                + "&scope=" + Uri.EscapeDataString("https://graph.microsoft.com/.default");

            Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                Post(url, "application/x-www-form-urlencoded", form, null));
            if (json != null && json.TryGetValue("access_token", out object token) && token is string text)
            {
                return text;
            }
            throw new InvalidOperationException("Microsoft 365 didn't return an access token.");
        }

        // Returns the sendMail request: the message as plain text.
        private static string BuildRequest(MailMessage message)
        {
            List<object> recipients = new List<object>();
            foreach (MailAddress recipient in message.To)
            {
                recipients.Add(new Dictionary<string, object>
                {
                    ["emailAddress"] = new Dictionary<string, object> { ["address"] = recipient.Address }
                });
            }

            Dictionary<string, object> from = new Dictionary<string, object> { ["address"] = message.From.Address };
            if (!string.IsNullOrEmpty(message.From.DisplayName))
            {
                from["name"] = message.From.DisplayName;
            }

            Dictionary<string, object> request = new Dictionary<string, object>
            {
                ["message"] = new Dictionary<string, object>
                {
                    ["subject"] = message.Subject ?? string.Empty,
                    ["body"] = new Dictionary<string, object>
                    {
                        ["contentType"] = "Text",
                        ["content"] = message.Body ?? string.Empty
                    },
                    ["from"] = new Dictionary<string, object> { ["emailAddress"] = from },
                    ["toRecipients"] = recipients
                }
            };
            return new JavaScriptSerializer().Serialize(request);
        }

        // Posts the body and returns the response's text. If the request fails, throws an exception with
        // Microsoft's error message.
        private static string Post(string url, string contentType, string body, string bearerToken)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = contentType;
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            if (bearerToken != null)
            {
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + bearerToken;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    return ReadText(response);
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse response)
            {
                using (response)
                {
                    // The original exception isn't kept as the inner exception: its message only repeats the HTTP
                    // status, which the new message already has.
                    throw new WebException(GetErrorMessage(ReadText(response), (int)response.StatusCode), ex.Status);
                }
            }
        }

        private static string ReadText(HttpWebResponse response)
        {
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        // Returns Microsoft's error message from an error response, with the HTTP status. Common errors start with
        // a plain explanation, and sign-in errors end with their support details. Each is its own paragraph.
        // If the response has no message, returns just the HTTP status.
        private static string GetErrorMessage(string responseText, int statusCode)
        {
            string summary = "Microsoft 365 returned an error";
            string message = null;
            string code = null;
            string supportDetails = null;
            try
            {
                Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(responseText);

                // A sign-in error: {"error": "invalid_client", "error_description": "AADSTS7000215: ..."}
                // The description starts with the error code.
                if (json != null && json.TryGetValue("error_description", out object description) && description is string descriptionText)
                {
                    summary = "Microsoft 365 sign-in failed";
                    SplitDescription(descriptionText, out message, out supportDetails);
                    code = message.Split(':')[0];
                }
                else if (json != null
                    && json.TryGetValue("error", out object error) && error is Dictionary<string, object> details
                    && details.TryGetValue("message", out object messageValue) && messageValue is string messageText)
                {
                    // A Graph error: {"error": {"code": "ErrorAccessDenied", "message": "..."}}
                    summary = "Microsoft 365 didn't send the email";
                    message = messageText;
                    code = details.TryGetValue("code", out object codeValue) ? codeValue as string : null;
                }
            }
            catch (Exception)
            {
                // The response isn't JSON, such as an HTML error page.
            }

            string explanation = GetExplanation(code, statusCode);
            if (message == null)
            {
                return explanation != null
                    ? $"{summary}: {explanation} (HTTP {statusCode})"
                    : $"Microsoft 365 returned HTTP status {statusCode}.";
            }

            // A sign-in message already starts with its code, so the code is only added for Graph errors.
            string status = code != null && !message.StartsWith(code, StringComparison.Ordinal)
                ? $"({code}, HTTP {statusCode})"
                : $"(HTTP {statusCode})";

            List<string> paragraphs = new List<string>();
            if (explanation != null)
            {
                paragraphs.Add($"{summary}: {explanation}");
                paragraphs.Add($"{message} {status}");
            }
            else
            {
                paragraphs.Add($"{summary}: {message} {status}");
            }
            if (supportDetails != null)
            {
                paragraphs.Add(supportDetails);
            }
            return string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        }

        // Splits a sign-in error's description into its message and its support details: the trace ID,
        // correlation ID, and timestamp, one per line. The support details are null if there are none.
        // Microsoft puts them after the message, either on the same line or on their own lines.
        private static void SplitDescription(string description, out string message, out string supportDetails)
        {
            string text = description.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
            int start = text.IndexOf(" Trace ID:", StringComparison.Ordinal);
            if (start < 0)
            {
                message = text.Trim();
                supportDetails = null;
                return;
            }

            message = text.Substring(0, start).Trim();
            supportDetails = text.Substring(start).Trim()
                .Replace(" Correlation ID:", Environment.NewLine + "Correlation ID:")
                .Replace(" Timestamp:", Environment.NewLine + "Timestamp:");
        }

        // Returns a plain explanation of a common error, or null for any other error.
        private static string GetExplanation(string code, int statusCode)
        {
            switch (code)
            {
                case "AADSTS7000215":
                    return "The client secret is wrong. Use the secret's Value, not its Secret ID.";
                case "AADSTS7000222":
                    return "The client secret has expired. Create a new one in the app registration's Certificates & secrets.";
                case "AADSTS700016":
                    return "The client ID wasn't found in this tenant. Check the client ID and tenant ID.";
                case "AADSTS90002":
                case "AADSTS900023":
                    return "The tenant ID wasn't found. Use the Directory (tenant) ID from Microsoft Entra.";
                case "ErrorAccessDenied":
                    return "The app isn't allowed to send as this mailbox. Check that it has the Mail.Send application permission, with admin consent.";
                case "ErrorInvalidUser":
                case "MailboxNotEnabledForRESTAPI":
                case "ResourceNotFound":
                    return "The From address isn't a Microsoft 365 mailbox in this tenant.";
            }

            if (statusCode == 429)
            {
                return "Microsoft 365 is limiting how often this app can send. Try again later.";
            }
            return null;
        }
    }
}
