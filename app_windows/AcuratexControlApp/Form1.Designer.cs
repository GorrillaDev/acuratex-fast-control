using Microsoft.AspNetCore.Components.WebView.WindowsForms;

namespace AcuratexControlApp;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private BlazorWebView blazorWebView;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.components = new System.ComponentModel.Container();
        this.blazorWebView = new Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView();
        this.SuspendLayout();
        // 
        // blazorWebView
        // 
        this.blazorWebView.Dock = System.Windows.Forms.DockStyle.Fill;
        this.blazorWebView.Location = new System.Drawing.Point(0, 0);
        this.blazorWebView.Margin = new System.Windows.Forms.Padding(0);
        this.blazorWebView.Name = "blazorWebView";
        this.blazorWebView.Padding = new System.Windows.Forms.Padding(0);
        this.blazorWebView.Size = new System.Drawing.Size(1320, 800);
        this.blazorWebView.TabIndex = 0;
        // 
        // Form1
        // 
        this.AutoScroll = false;
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.BackColor = System.Drawing.Color.White;
        this.ClientSize = new System.Drawing.Size(1320, 800);
        this.Controls.Add(this.blazorWebView);
        this.ControlBox = false;
        this.Font = new System.Drawing.Font("Segoe UI", 9F);
        this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
        this.Margin = new System.Windows.Forms.Padding(0);
        this.MinimumSize = new System.Drawing.Size(1120, 720);
        this.Name = "Form1";
        this.Padding = new System.Windows.Forms.Padding(0);
        this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        this.Text = "Acuratex Control App";
        this.ResumeLayout(false);
    }
}
