using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace tcpTrigger
{
    // Posts messages to webhooks, such as a Slack or Discord channel. The title is optional, and is shown in bold
    // or as the notification's title. Each method throws an exception if the request fails.
    internal static class WebhookSender
    {
        // How long to wait for each request.
        private const int TimeoutMilliseconds = 30000;

        // The longest messages each platform accepts. Longer messages are cut short to fit.
        private const int DiscordMaxLength = 2000;
        private const int GoogleChatMaxLength = 4000;
        private const int NtfyMaxLength = 4000;

        // Returns true if the URL is an absolute http or https URL.
        public static bool IsValidUrl(string url)
        {
            return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        // Slack, and Slack-compatible webhooks such as Mattermost and Rocket.Chat.
        public static void SendSlack(string url, string title, string message)
        {
            string text = EscapeSlack(message);
            if (!string.IsNullOrWhiteSpace(title))
            {
                text = $"*{EscapeSlack(title.Trim())}*\n{text}";
            }
            PostJson(url, "Slack", new Dictionary<string, object> { ["text"] = text });
        }

        public static void SendDiscord(string url, string title, string message)
        {
            string content = EscapeDiscord(message);
            if (!string.IsNullOrWhiteSpace(title))
            {
                content = $"**{EscapeDiscord(title.Trim())}**\n{content}";
            }
            PostJson(url, "Discord", new Dictionary<string, object>
            {
                ["content"] = Shorten(content, DiscordMaxLength),
                // Don't let text such as @everyone in a hostname ping anyone.
                ["allowed_mentions"] = new Dictionary<string, object> { ["parse"] = new string[0] }
            });
        }

        public static void SendGoogleChat(string url, string title, string message)
        {
            string text = message;
            if (!string.IsNullOrWhiteSpace(title))
            {
                text = $"*{title.Trim()}*\n{text}";
            }
            PostJson(url, "Google Chat", new Dictionary<string, object> { ["text"] = Shorten(text, GoogleChatMaxLength) });
        }

        // ntfy takes the message as plain text, and the title in a header.
        public static void SendNtfy(string url, string title, string message)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(title))
            {
                headers["Title"] = EncodeHeader(title);
            }
            Post(url, "ntfy", "text/plain; charset=utf-8", Shorten(message, NtfyMaxLength), headers);
        }

        private static void PostJson(string url, string platform, Dictionary<string, object> request)
        {
            Post(url, platform, "application/json", new JavaScriptSerializer().Serialize(request), null);
        }

        // Posts the body. If the request fails, throws an exception with the platform's error message.
        private static void Post(string url, string platform, string contentType, string body, Dictionary<string, string> headers)
        {
            if (!IsValidUrl(url))
            {
                throw new ArgumentException("The webhook URL isn't a valid http or https URL.");
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url.Trim());
            request.Method = "POST";
            request.ContentType = contentType;
            request.Timeout = TimeoutMilliseconds;
            request.ReadWriteTimeout = TimeoutMilliseconds;
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    request.Headers[header.Key] = header.Value;
                }
            }

            byte[] bytes = Encoding.UTF8.GetBytes(body);
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            try
            {
                using (request.GetResponse())
                {
                    // The response isn't needed. A success status means the message was accepted.
                }
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse response)
            {
                using (response)
                {
                    // The original exception isn't kept as the inner exception: its message only repeats the HTTP
                    // status, which the new message already has.
                    throw new WebException(GetErrorMessage(platform, ReadText(response), (int)response.StatusCode), ex.Status);
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

        // Returns the platform's error message from an error response, with the HTTP status. Common errors start with
        // a plain explanation, in its own paragraph. If the response has no message, returns just the HTTP status.
        private static string GetErrorMessage(string platform, string responseText, int statusCode)
        {
            string message = GetResponseMessage(responseText);
            string explanation = GetExplanation(platform, message, statusCode);
            if (message == null)
            {
                return explanation != null
                    ? $"{platform} returned an error: {explanation} (HTTP {statusCode})"
                    : $"{platform} returned HTTP status {statusCode}.";
            }

            return explanation != null
                ? $"{platform} returned an error: {explanation}{Environment.NewLine}{Environment.NewLine}{message} (HTTP {statusCode})"
                : $"{platform} returned an error: {message} (HTTP {statusCode})";
        }

        // Returns the error message in a response, or null if there's none. Each platform puts it somewhere else:
        //   Discord:     {"message": "Unknown Webhook", "code": 10015}
        //   Google Chat: {"error": {"code": 404, "message": "...", "status": "NOT_FOUND"}}
        //   ntfy:        {"code": 40301, "http": 403, "error": "forbidden"}
        //   Slack:       plain text, such as "no_service"
        private static string GetResponseMessage(string responseText)
        {
            string text = responseText?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            try
            {
                Dictionary<string, object> json = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(text);
                if (json != null)
                {
                    json.TryGetValue("error", out object error);
                    if (error is Dictionary<string, object> details
                        && details.TryGetValue("message", out object detailsMessage) && detailsMessage is string detailsText)
                    {
                        return detailsText;
                    }
                    if (json.TryGetValue("message", out object message) && message is string messageText)
                    {
                        return messageText;
                    }
                    if (error is string errorText)
                    {
                        return errorText;
                    }
                }
                return null;
            }
            catch (Exception)
            {
                // Not JSON. Short plain text is the message, but not an HTML error page.
                return text.Length <= 200 && !text.StartsWith("<", StringComparison.Ordinal) ? text : null;
            }
        }

        // Returns a plain explanation of a common error, or null for any other error.
        private static string GetExplanation(string platform, string message, int statusCode)
        {
            switch (message)
            {
                case "no_service":
                case "no_team":
                case "invalid_token":
                case "channel_not_found":
                    return "The webhook wasn't found. It may have been removed, or the URL is wrong.";
                case "channel_is_archived":
                    return "The channel is archived.";
                case "action_prohibited":
                    return "A Slack admin has restricted this webhook.";
            }

            switch (statusCode)
            {
                case 401:
                case 403:
                    return "Access was denied. Check that the URL is complete and the webhook still exists.";
                case 404:
                    return "The webhook wasn't found. It may have been removed, or the URL is wrong.";
                case 413:
                    return "The message is too large.";
                case 429:
                    return $"{platform} is limiting how often messages can be sent. Try again later, or raise the rate limit.";
            }
            return null;
        }

        // Slack treats &, <, and > as markup, such as <!channel> to ping a channel.
        private static string EscapeSlack(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        // Puts a backslash before each character Discord uses for formatting, so the text shows as it is.
        private static string EscapeDiscord(string text)
        {
            StringBuilder escaped = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if ("\\*_~`|>#[]".IndexOf(c) >= 0)
                {
                    escaped.Append('\\');
                }
                escaped.Append(c);
            }
            return escaped.ToString();
        }

        // HTTP headers can only hold ASCII text on one line. Other text is sent as UTF-8 in an RFC 2047 encoded word.
        private static string EncodeHeader(string text)
        {
            string line = text.Replace("\r", " ").Replace("\n", " ").Trim();
            foreach (char c in line)
            {
                if (c < 32 || c > 126)
                {
                    return "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(line)) + "?=";
                }
            }
            return line;
        }

        // Cuts text that's longer than maxLength, ending it with "...".
        private static string Shorten(string text, int maxLength)
        {
            if (text.Length <= maxLength)
            {
                return text;
            }

            int length = maxLength - 3;

            // Don't split an emoji or other character that takes two UTF-16 characters.
            if (char.IsHighSurrogate(text[length - 1]))
            {
                length--;
            }
            return text.Substring(0, length) + "...";
        }
    }
}
