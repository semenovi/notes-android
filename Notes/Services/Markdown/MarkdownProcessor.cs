using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Notes.Services.Storage;

namespace Notes.Services.Markdown
{
    public class MarkdownProcessor
    {
        private readonly MediaStorage _mediaStorage;
        private readonly List<ISyntaxExtension> _extensions;

        public MarkdownProcessor(MediaStorage mediaStorage)
        {
            _mediaStorage = mediaStorage;
            _extensions = new List<ISyntaxExtension>();
        }

        public string ConvertToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            // Apply extensions
            foreach (var extension in _extensions)
            {
                markdown = extension.Process(markdown);
            }

            markdown = ProcessMediaLinks(markdown);
            markdown = ProcessBasicMarkdown(markdown);

            return markdown;
        }

        public string ProcessMediaLinks(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            // Find media links with the pattern ![alt](media:id)
            var regex = new Regex(@"!\[(.*?)\]\(media:(.*?)\)");
            return regex.Replace(markdown, match =>
            {
                string alt = match.Groups[1].Value;
                string mediaId = match.Groups[2].Value;
                var mediaItem = _mediaStorage.GetMedia(mediaId);

                if (mediaItem == null)
                    return $"[Media not found: {mediaId}]";

                string extension = mediaItem.FileType.ToLowerInvariant();
                if (IsImageExtension(extension))
                {
                    // For images, create an img tag
                    return $"<img src=\"file://{mediaItem.StoragePath}\" alt=\"{alt}\" />";
                }
                else
                {
                    // For other files, create a link
                    return $"<a href=\"file://{mediaItem.StoragePath}\">{alt}</a>";
                }
            });
        }

        private string ProcessBasicMarkdown(string markdown)
        {
            // This is a very basic implementation
            // In a real app, you would use a library like Markdig
            
            var html = new StringBuilder();
            var lines = markdown.Split('\n');
            bool inParagraph = false;
            bool inCodeBlock = false;
            bool inList = false;

            foreach (var line in lines)
            {
                string trimmedLine = line.Trim();

                // Empty line
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    if (inParagraph)
                    {
                        html.AppendLine("</p>");
                        inParagraph = false;
                    }
                    if (inList)
                    {
                        html.AppendLine("</ul>");
                        inList = false;
                    }
                    continue;
                }

                // Code block
                if (trimmedLine.StartsWith("```"))
                {
                    if (inCodeBlock)
                    {
                        html.AppendLine("</code></pre>");
                        inCodeBlock = false;
                    }
                    else
                    {
                        if (inParagraph)
                        {
                            html.AppendLine("</p>");
                            inParagraph = false;
                        }
                        html.AppendLine("<pre><code>");
                        inCodeBlock = true;
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    html.AppendLine(System.Net.WebUtility.HtmlEncode(line));
                    continue;
                }

                // Headers
                if (trimmedLine.StartsWith("# "))
                {
                    if (inParagraph)
                    {
                        html.AppendLine("</p>");
                        inParagraph = false;
                    }
                    html.AppendLine($"<h1>{ProcessInlineMarkdown(trimmedLine.Substring(2))}</h1>");
                    continue;
                }
                if (trimmedLine.StartsWith("## "))
                {
                    if (inParagraph)
                    {
                        html.AppendLine("</p>");
                        inParagraph = false;
                    }
                    html.AppendLine($"<h2>{ProcessInlineMarkdown(trimmedLine.Substring(3))}</h2>");
                    continue;
                }
                if (trimmedLine.StartsWith("### "))
                {
                    if (inParagraph)
                    {
                        html.AppendLine("</p>");
                        inParagraph = false;
                    }
                    html.AppendLine($"<h3>{ProcessInlineMarkdown(trimmedLine.Substring(4))}</h3>");
                    continue;
                }

                // Lists
                if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                {
                    if (inParagraph)
                    {
                        html.AppendLine("</p>");
                        inParagraph = false;
                    }
                    if (!inList)
                    {
                        html.AppendLine("<ul>");
                        inList = true;
                    }
                    html.AppendLine($"<li>{ProcessInlineMarkdown(trimmedLine.Substring(2))}</li>");
                    continue;
                }

                // Regular paragraph
                if (!inParagraph)
                {
                    html.AppendLine("<p>");
                    inParagraph = true;
                }
                html.AppendLine(ProcessInlineMarkdown(line));
            }

            // Close any open tags
            if (inParagraph)
                html.AppendLine("</p>");
            if (inList)
                html.AppendLine("</ul>");
            if (inCodeBlock)
                html.AppendLine("</code></pre>");

            return html.ToString();
        }

        private string ProcessInlineMarkdown(string text)
        {
            // Process bold
            text = Regex.Replace(text, @"\*\*(.*?)\*\*", "<strong>$1</strong>");
            text = Regex.Replace(text, @"__(.*?)__", "<strong>$1</strong>");

            // Process italic
            text = Regex.Replace(text, @"\*(.*?)\*", "<em>$1</em>");
            text = Regex.Replace(text, @"_(.*?)_", "<em>$1</em>");

            // Process inline code
            text = Regex.Replace(text, @"`(.*?)`", "<code>$1</code>");

            // Process links
            text = Regex.Replace(text, @"\[(.*?)\]\((.*?)\)", "<a href=\"$2\">$1</a>");

            return text;
        }

        public void RegisterExtension(ISyntaxExtension extension)
        {
            if (!_extensions.Contains(extension))
            {
                _extensions.Add(extension);
            }
        }

        private bool IsImageExtension(string extension)
        {
            return extension == ".jpg" || extension == ".jpeg" || 
                   extension == ".png" || extension == ".gif" || 
                   extension == ".bmp" || extension == ".webp";
        }
    }
}