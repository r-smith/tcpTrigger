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
                    throw new WebException(GetErrorMessage(ReadText(response), (int)response.StatusCode), ex);
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

        // Returns Microsoft's error message from an error response, or the HTTP status if it has none.
        private static string GetErrorMessage(string responseText, int statusCode)
        {
            try
            {
                Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(responseText);

                // A sign-in error: {"error": "invalid_client", "error_description": "AADSTS7000215: ..."}
                // The description ends with trace and correlation IDs on their own lines, so only its first
                // line is kept.
                if (json != null && json.TryGetValue("error_description", out object description) && description is string descriptionText)
                {
                    return "Microsoft 365 sign-in failed: " + descriptionText.Split('\r', '\n')[0];
                }

                // A Graph error: {"error": {"code": "ErrorAccessDenied", "message": "..."}}
                if (json != null
                    && json.TryGetValue("error", out object error) && error is Dictionary<string, object> details
                    && details.TryGetValue("message", out object message) && message is string messageText)
                {
                    return "Microsoft 365 didn't send the email: " + messageText;
                }
            }
            catch (Exception)
            {
                // The response isn't JSON, such as an HTML error page.
            }
            return $"Microsoft 365 returned HTTP status {statusCode}.";
        }
    }
}
