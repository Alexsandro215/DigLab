using System;
using System.Windows.Forms;

namespace DigBit
{
    partial class Login
    {
        /// <summary>
        /// Variable del diseñador necesaria.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Limpiar los recursos que se estén usando.
        /// </summary>
        /// <param name="disposing">true si los recursos administrados se deben desechar; false en caso contrario.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Código generado por el Diseñador de Windows Forms

        /// <summary>
        /// Método necesario para admitir el Diseñador. No se puede modificar
        /// el contenido de este método con el editor de código.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Login));
            this.panelLogin = new System.Windows.Forms.Panel();
            this.imgMinimizar = new System.Windows.Forms.PictureBox();
            this.imgSalir = new System.Windows.Forms.PictureBox();
            this.linkPasswd = new System.Windows.Forms.LinkLabel();
            this.btnRegistro = new DigBit.RJControls.RJButton();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.txtUsuario = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.splitter2 = new System.Windows.Forms.Splitter();
            this.btnIniciarSesion = new DigBit.RJControls.RJButton();
            this.splitter1 = new System.Windows.Forms.Splitter();
            this.imgPasswd = new System.Windows.Forms.PictureBox();
            this.imgUsuario = new System.Windows.Forms.PictureBox();
            this.imgLogo = new System.Windows.Forms.PictureBox();
            this.panelLogin.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.imgMinimizar)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgSalir)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgPasswd)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgUsuario)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgLogo)).BeginInit();
            this.SuspendLayout();
            // 
            // panelLogin
            // 
            this.panelLogin.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("panelLogin.BackgroundImage")));
            this.panelLogin.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
            this.panelLogin.Controls.Add(this.imgMinimizar);
            this.panelLogin.Controls.Add(this.imgSalir);
            this.panelLogin.Controls.Add(this.linkPasswd);
            this.panelLogin.Controls.Add(this.btnRegistro);
            this.panelLogin.Controls.Add(this.txtPassword);
            this.panelLogin.Controls.Add(this.txtUsuario);
            this.panelLogin.Controls.Add(this.label3);
            this.panelLogin.Controls.Add(this.label2);
            this.panelLogin.Controls.Add(this.splitter2);
            this.panelLogin.Controls.Add(this.btnIniciarSesion);
            this.panelLogin.Controls.Add(this.splitter1);
            this.panelLogin.Controls.Add(this.imgPasswd);
            this.panelLogin.Controls.Add(this.imgUsuario);
            this.panelLogin.Controls.Add(this.imgLogo);
            this.panelLogin.Location = new System.Drawing.Point(-8, -6);
            this.panelLogin.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.panelLogin.Name = "panelLogin";
            this.panelLogin.Size = new System.Drawing.Size(447, 528);
            this.panelLogin.TabIndex = 1;
            // 
            // imgMinimizar
            // 
            this.imgMinimizar.BackColor = System.Drawing.Color.Transparent;
            this.imgMinimizar.Image = ((System.Drawing.Image)(resources.GetObject("imgMinimizar.Image")));
            this.imgMinimizar.Location = new System.Drawing.Point(324, 21);
            this.imgMinimizar.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.imgMinimizar.Name = "imgMinimizar";
            this.imgMinimizar.Size = new System.Drawing.Size(31, 28);
            this.imgMinimizar.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.imgMinimizar.TabIndex = 21;
            this.imgMinimizar.TabStop = false;
            this.imgMinimizar.Click += new System.EventHandler(this.imgMinimizar_Click);
            // 
            // imgSalir
            // 
            this.imgSalir.BackColor = System.Drawing.Color.Transparent;
            this.imgSalir.Cursor = System.Windows.Forms.Cursors.Hand;
            this.imgSalir.Image = ((System.Drawing.Image)(resources.GetObject("imgSalir.Image")));
            this.imgSalir.Location = new System.Drawing.Point(363, 21);
            this.imgSalir.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.imgSalir.Name = "imgSalir";
            this.imgSalir.Size = new System.Drawing.Size(31, 28);
            this.imgSalir.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.imgSalir.TabIndex = 20;
            this.imgSalir.TabStop = false;
            this.imgSalir.Click += new System.EventHandler(this.imgSalir_Click);
            // 
            // linkPasswd
            // 
            this.linkPasswd.ActiveLinkColor = System.Drawing.Color.Green;
            this.linkPasswd.AutoSize = true;
            this.linkPasswd.BackColor = System.Drawing.Color.White;
            this.linkPasswd.DisabledLinkColor = System.Drawing.Color.Transparent;
            this.linkPasswd.Font = new System.Drawing.Font("Century Gothic", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.linkPasswd.LinkColor = System.Drawing.Color.DarkGreen;
            this.linkPasswd.Location = new System.Drawing.Point(124, 364);
            this.linkPasswd.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.linkPasswd.Name = "linkPasswd";
            this.linkPasswd.Padding = new System.Windows.Forms.Padding(5, 3, 5, 3);
            this.linkPasswd.Size = new System.Drawing.Size(161, 16);
            this.linkPasswd.TabIndex = 3;
            this.linkPasswd.TabStop = true;
            this.linkPasswd.Text = "¿Olvidaste tu contraseña?";
            this.linkPasswd.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkPasswd_LinkClicked_1);
            // 
            // btnRegistro
            // 
            this.btnRegistro.BackColor = System.Drawing.Color.White;
            this.btnRegistro.BackgroundColor = System.Drawing.Color.White;
            this.btnRegistro.BorderColor = System.Drawing.Color.DarkGreen;
            this.btnRegistro.BorderRadius = 20;
            this.btnRegistro.BorderSize = 2;
            this.btnRegistro.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRegistro.FlatAppearance.BorderSize = 0;
            this.btnRegistro.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRegistro.Font = new System.Drawing.Font("Century Gothic", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnRegistro.ForeColor = System.Drawing.Color.DarkGreen;
            this.btnRegistro.Location = new System.Drawing.Point(111, 403);
            this.btnRegistro.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnRegistro.Name = "btnRegistro";
            this.btnRegistro.Size = new System.Drawing.Size(201, 46);
            this.btnRegistro.TabIndex = 4;
            this.btnRegistro.Text = "CREAR CUENTA";
            this.btnRegistro.TextColor = System.Drawing.Color.DarkGreen;
            this.btnRegistro.UseVisualStyleBackColor = false;
            this.btnRegistro.Click += new System.EventHandler(this.btnRegistro_Click);
            // 
            // txtPassword
            //
            this.txtPassword.AccessibleName = "Contraseña";
            this.txtPassword.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtPassword.Font = new System.Drawing.Font("Century Gothic", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtPassword.ForeColor = System.Drawing.Color.DarkGreen;
            this.txtPassword.Location = new System.Drawing.Point(95, 213);
            this.txtPassword.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Size = new System.Drawing.Size(180, 25);
            this.txtPassword.TabIndex = 1;
            this.txtPassword.UseSystemPasswordChar = true;
            //
            // txtUsuario
            //
            this.txtUsuario.AccessibleName = "Usuario";
            this.txtUsuario.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtUsuario.Font = new System.Drawing.Font("Century Gothic", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtUsuario.ForeColor = System.Drawing.Color.DarkGreen;
            this.txtUsuario.Location = new System.Drawing.Point(95, 131);
            this.txtUsuario.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.txtUsuario.Name = "txtUsuario";
            this.txtUsuario.Size = new System.Drawing.Size(133, 25);
            this.txtUsuario.TabIndex = 0;
            //
            // label3
            //
            this.label3.AutoSize = true;
            this.label3.BackColor = System.Drawing.SystemColors.Window;
            this.label3.ForeColor = System.Drawing.Color.DarkGreen;
            this.label3.Location = new System.Drawing.Point(92, 224);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(126, 16);
            this.label3.TabIndex = 15;
            this.label3.Text = "_________________";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.BackColor = System.Drawing.SystemColors.Window;
            this.label2.ForeColor = System.Drawing.Color.DarkGreen;
            this.label2.Location = new System.Drawing.Point(91, 144);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(126, 16);
            this.label2.TabIndex = 14;
            this.label2.Text = "_________________";
            // 
            // splitter2
            // 
            this.splitter2.Location = new System.Drawing.Point(4, 0);
            this.splitter2.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.splitter2.Name = "splitter2";
            this.splitter2.Size = new System.Drawing.Size(4, 528);
            this.splitter2.TabIndex = 11;
            this.splitter2.TabStop = false;
            // 
            // btnIniciarSesion
            // 
            this.btnIniciarSesion.BackColor = System.Drawing.Color.White;
            this.btnIniciarSesion.BackgroundColor = System.Drawing.Color.White;
            this.btnIniciarSesion.BorderColor = System.Drawing.Color.DarkGreen;
            this.btnIniciarSesion.BorderRadius = 20;
            this.btnIniciarSesion.BorderSize = 2;
            this.btnIniciarSesion.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnIniciarSesion.FlatAppearance.BorderSize = 0;
            this.btnIniciarSesion.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnIniciarSesion.Font = new System.Drawing.Font("Century Gothic", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnIniciarSesion.ForeColor = System.Drawing.Color.DarkGreen;
            this.btnIniciarSesion.Location = new System.Drawing.Point(111, 306);
            this.btnIniciarSesion.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.btnIniciarSesion.Name = "btnIniciarSesion";
            this.btnIniciarSesion.Size = new System.Drawing.Size(201, 46);
            this.btnIniciarSesion.TabIndex = 2;
            this.btnIniciarSesion.Text = "INICIAR  SESIÓN";
            this.btnIniciarSesion.TextColor = System.Drawing.Color.DarkGreen;
            this.btnIniciarSesion.UseVisualStyleBackColor = false;
            this.btnIniciarSesion.Click += new System.EventHandler(this.btnIniciarSesion_Click);
            // 
            // splitter1
            // 
            this.splitter1.Location = new System.Drawing.Point(0, 0);
            this.splitter1.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.splitter1.Name = "splitter1";
            this.splitter1.Size = new System.Drawing.Size(4, 528);
            this.splitter1.TabIndex = 8;
            this.splitter1.TabStop = false;
            // 
            // imgPasswd
            // 
            this.imgPasswd.BackColor = System.Drawing.SystemColors.ButtonHighlight;
            this.imgPasswd.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("imgPasswd.BackgroundImage")));
            this.imgPasswd.Location = new System.Drawing.Point(15, 195);
            this.imgPasswd.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.imgPasswd.Name = "imgPasswd";
            this.imgPasswd.Size = new System.Drawing.Size(71, 62);
            this.imgPasswd.TabIndex = 5;
            this.imgPasswd.TabStop = false;
            // 
            // imgUsuario
            // 
            this.imgUsuario.BackColor = System.Drawing.SystemColors.ButtonHighlight;
            this.imgUsuario.BackgroundImage = ((System.Drawing.Image)(resources.GetObject("imgUsuario.BackgroundImage")));
            this.imgUsuario.Location = new System.Drawing.Point(15, 113);
            this.imgUsuario.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.imgUsuario.Name = "imgUsuario";
            this.imgUsuario.Size = new System.Drawing.Size(68, 63);
            this.imgUsuario.TabIndex = 4;
            this.imgUsuario.TabStop = false;
            //
            // imgLogo
            //
            this.imgLogo.AccessibleName = "Tecnológico de Estudios Superiores de Chimalhuacán";
            this.imgLogo.BackColor = System.Drawing.Color.Transparent;
            this.imgLogo.Image = global::DigBit.Properties.Resources.logoteschi;
            this.imgLogo.Location = new System.Drawing.Point(19, 32);
            this.imgLogo.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.imgLogo.Name = "imgLogo";
            this.imgLogo.Size = new System.Drawing.Size(160, 64);
            this.imgLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.imgLogo.TabIndex = 22;
            this.imgLogo.TabStop = false;
            //
            // Login
            //
            this.AcceptButton = this.btnIniciarSesion;
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(400, 492);
            this.Controls.Add(this.panelLogin);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.Name = "Login";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Iniciar sesión - DigBit";
            this.panelLogin.ResumeLayout(false);
            this.panelLogin.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.imgMinimizar)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgSalir)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgPasswd)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgUsuario)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.imgLogo)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panelLogin;
        private System.Windows.Forms.PictureBox imgUsuario;
        private System.Windows.Forms.PictureBox imgPasswd;
        private System.Windows.Forms.Splitter splitter1;
        private RJControls.RJButton btnIniciarSesion;
        private System.Windows.Forms.Splitter splitter2;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.TextBox txtUsuario;
        private System.Windows.Forms.TextBox txtPassword;
        private RJControls.RJButton btnRegistro;
        private System.Windows.Forms.LinkLabel linkPasswd;
        private System.Windows.Forms.PictureBox imgSalir;
        private System.Windows.Forms.PictureBox imgMinimizar;
        private System.Windows.Forms.PictureBox imgLogo;
    }
}

