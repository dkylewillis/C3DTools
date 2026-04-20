using System.Windows.Forms;

namespace C3DTools.UI
{
    public class BasinLanduseResultForm : Form
    {
        private readonly string _clipboardContent;
        private RichTextBox? _textBox;
        private Button? _copyButton;
        private Button? _closeButton;
        private Label? _tipLabel;

        public BasinLanduseResultForm(string displayText, string clipboardText)
        {
            _clipboardContent = clipboardText;
            BuildUI(displayText);
        }

        private void BuildUI(string displayText)
        {
            Text = "Basin Landuse Results";
            Width = 900;
            Height = 500;
            MinimumSize = new System.Drawing.Size(500, 300);
            StartPosition = FormStartPosition.CenterParent;

            _copyButton = new Button
            {
                Text = "Copy to Clipboard",
                Width = 140,
                Height = 28,
                Anchor = AnchorStyles.None
            };
            _copyButton.Click += (s, e) =>
            {
                System.Windows.Forms.Clipboard.SetText(_clipboardContent);
                _copyButton.Text = "Copied!";
            };

            _closeButton = new Button
            {
                Text = "Close",
                Width = 80,
                Height = 28,
                Anchor = AnchorStyles.None
            };
            _closeButton.Click += (s, e) => Close();

            _tipLabel = new Label
            {
                Text = "Click 'Copy to Clipboard' then switch to Excel and press Ctrl+V.",
                AutoSize = true,
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font("Segoe UI", 9f),
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Bottom
            };

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(4)
            };
            bottomPanel.Controls.Add(_tipLabel);
            bottomPanel.Controls.Add(_copyButton);
            bottomPanel.Controls.Add(_closeButton);

            _tipLabel.Location = new System.Drawing.Point(4, 10);
            _copyButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            _closeButton.Anchor = AnchorStyles.Right | AnchorStyles.Top;

            bottomPanel.Layout += (s, e) =>
            {
                int panelRight = bottomPanel.ClientSize.Width - 4;
                _closeButton.Location = new System.Drawing.Point(panelRight - _closeButton.Width, 8);
                _copyButton.Location = new System.Drawing.Point(_closeButton.Left - _copyButton.Width - 6, 8);
            };

            _textBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = new System.Drawing.Font("Consolas", 10f),
                ReadOnly = true,
                Text = displayText,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false,
                BackColor = System.Drawing.Color.White
            };

            Controls.Add(_textBox);
            Controls.Add(bottomPanel);
        }
    }
}
