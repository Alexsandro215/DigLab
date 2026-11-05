using System;
using System.Windows.Forms;

namespace DigLab
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
            this.panelTarjeta = new System.Windows.Forms.Panel();
            this.imgLogo = new System.Windows.Forms.PictureBox();
            this.txtUsuario = new System.Windows.Forms.TextBox();
            this.txtPassword = new System.Windows.Forms.TextBox();
            this.btnIniciarSesion = new DigLab.RJControls.RJButton();
            this.linkPasswd = new System.Windows.Forms.LinkLabel();
            this.btnRegistro = new DigLab.RJControls.RJButton();
            this.imgMinimizar = new System.Windows.Forms.Label();
            this.imgSalir = new System.Windows.Forms.Label();
            this.panelTarjeta.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.imgLogo)).BeginInit();
            this.SuspendLayout();
            //
            // panelTarjeta
            //
            this.panelTarjeta.BackColor = System.Drawing.Color.White;
            this.panelTarjeta.Controls.Add(this.imgLogo);
            this.panelTarjeta.Controls.Add(this.txtUsuario);
            this.panelTarjeta.Controls.Add(this.txtPassword);
            this.panelTarjeta.Controls.Add(this.btnIniciarSesion);
            this.panelTarjeta.Controls.Add(this.linkPasswd);
            this.panelTarjeta.Controls.Add(this.btnRegistro);
            this.panelTarjeta.Location = new System.Drawing.Point(52, 52);
            this.panelTarjeta.Name = "panelTarjeta";
            this.panelTarjeta.Size = new System.Drawing.Size(376, 536);
            this.panelTarjeta.TabIndex = 0;
            //
            // imgLogo
            //
            this.imgLogo.AccessibleName = "Tecnológico de Estudios Superiores de Chimalhuacán";
            this.imgLogo.BackColor = System.Drawing.Color.White;
            this.imgLogo.Image = global::DigLab.Properties.Resources.logoteschi;
            this.imgLogo.Location = new System.Drawing.Point(98, 8);
            this.imgLogo.Name = "imgLogo";
            this.imgLogo.Size = new System.Drawing.Size(180, 72);
            this.imgLogo.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.imgLogo.TabIndex = 10;
            this.imgLogo.TabStop = false;
            //
            // txtUsuario
            //
            this.txtUsuario.AccessibleName = "Usuario";
            this.txtUsuario.BackColor = System.Drawing.Color.White;
            this.txtUsuario.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtUsuario.Font = new System.Drawing.Font("Century Gothic", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtUsuario.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.txtUsuario.Location = new System.Drawing.Point(44, 200);
            this.txtUsuario.Name = "txtUsuario";
            this.txtUsuario.Size = new System.Drawing.Size(280, 20);
            this.txtUsuario.TabIndex = 0;
            //
            // txtPassword
            //
            this.txtPassword.AccessibleName = "Contraseña";
            this.txtPassword.BackColor = System.Drawing.Color.White;
            this.txtPassword.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtPassword.Font = new System.Drawing.Font("Century Gothic", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtPassword.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(30)))), ((int)(((byte)(30)))), ((int)(((byte)(30)))));
            this.txtPassword.Location = new System.Drawing.Point(44, 278);
            this.txtPassword.Name = "txtPassword";
            this.txtPassword.Size = new System.Drawing.Size(250, 20);
            this.txtPassword.TabIndex = 1;
            this.txtPassword.UseSystemPasswordChar = true;
            //
            // btnIniciarSesion
            //
            this.btnIniciarSesion.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(112)))), ((int)(((byte)(64)))));
            this.btnIniciarSesion.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(112)))), ((int)(((byte)(64)))));
            this.btnIniciarSesion.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(112)))), ((int)(((byte)(64)))));
            this.btnIniciarSesion.BorderRadius = 12;
            this.btnIniciarSesion.BorderSize = 0;
            this.btnIniciarSesion.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnIniciarSesion.FlatAppearance.BorderSize = 0;
            this.btnIniciarSesion.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(84)))), ((int)(((byte)(48)))));
            this.btnIniciarSesion.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(96)))), ((int)(((byte)(55)))));
            this.btnIniciarSesion.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnIniciarSesion.Font = new System.Drawing.Font("Century Gothic", 11.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnIniciarSesion.ForeColor = System.Drawing.Color.White;
            this.btnIniciarSesion.Location = new System.Drawing.Point(24, 360);
            this.btnIniciarSesion.Name = "btnIniciarSesion";
            this.btnIniciarSesion.Size = new System.Drawing.Size(328, 46);
            this.btnIniciarSesion.TabIndex = 2;
            this.btnIniciarSesion.Text = "Iniciar sesión";
            this.btnIniciarSesion.TextColor = System.Drawing.Color.White;
            this.btnIniciarSesion.UseVisualStyleBackColor = false;
            this.btnIniciarSesion.Click += new System.EventHandler(this.btnIniciarSesion_Click);
            //
            // linkPasswd
            //
            this.linkPasswd.ActiveLinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(120)))), ((int)(((byte)(184)))), ((int)(((byte)(56)))));
            this.linkPasswd.AutoSize = true;
            this.linkPasswd.BackColor = System.Drawing.Color.White;
            this.linkPasswd.DisabledLinkColor = System.Drawing.Color.Gray;
            this.linkPasswd.Font = new System.Drawing.Font("Century Gothic", 9.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.linkPasswd.LinkBehavior = System.Windows.Forms.LinkBehavior.HoverUnderline;
            this.linkPasswd.LinkColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(112)))), ((int)(((byte)(64)))));
            this.linkPasswd.Location = new System.Drawing.Point(110, 418);
            this.linkPasswd.Name = "linkPasswd";
            this.linkPasswd.Size = new System.Drawing.Size(165, 17);
            this.linkPasswd.TabIndex = 3;
            this.linkPasswd.TabStop = true;
            this.linkPasswd.Text = "¿Olvidaste tu contraseña?";
            this.linkPasswd.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.linkPasswd_LinkClicked_1);
            //
            // btnRegistro
            //
            this.btnRegistro.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(243)))), ((int)(((byte)(236)))));
            this.btnRegistro.BackgroundColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(243)))), ((int)(((byte)(236)))));
            this.btnRegistro.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(232)))), ((int)(((byte)(243)))), ((int)(((byte)(236)))));
            this.btnRegistro.BorderRadius = 12;
            this.btnRegistro.BorderSize = 0;
            this.btnRegistro.Cursor = System.Windows.Forms.Cursors.Hand;
            this.btnRegistro.FlatAppearance.BorderSize = 0;
            this.btnRegistro.FlatAppearance.MouseDownBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(200)))), ((int)(((byte)(228)))), ((int)(((byte)(210)))));
            this.btnRegistro.FlatAppearance.MouseOverBackColor = System.Drawing.Color.FromArgb(((int)(((byte)(216)))), ((int)(((byte)(236)))), ((int)(((byte)(223)))));
            this.btnRegistro.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRegistro.Font = new System.Drawing.Font("Century Gothic", 10.5F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnRegistro.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(112)))), ((int)(((byte)(64)))));
            this.btnRegistro.Location = new System.Drawing.Point(24, 470);
            this.btnRegistro.Name = "btnRegistro";
            this.btnRegistro.Size = new System.Drawing.Size(328, 42);
            this.btnRegistro.TabIndex = 4;
            this.btnRegistro.Text = "Crear cuenta";
            this.btnRegistro.TextColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(112)))), ((int)(((byte)(64)))));
            this.btnRegistro.UseVisualStyleBackColor = false;
            this.btnRegistro.Click += new System.EventHandler(this.btnRegistro_Click);
            //
            // imgMinimizar
            //
            this.imgMinimizar.AccessibleName = "Minimizar";
            this.imgMinimizar.BackColor = System.Drawing.Color.Transparent;
            this.imgMinimizar.Cursor = System.Windows.Forms.Cursors.Hand;
            this.imgMinimizar.ForeColor = System.Drawing.Color.White;
            this.imgMinimizar.Location = new System.Drawing.Point(396, 8);
            this.imgMinimizar.Name = "imgMinimizar";
            this.imgMinimizar.Size = new System.Drawing.Size(36, 32);
            this.imgMinimizar.TabIndex = 21;
            this.imgMinimizar.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.imgMinimizar.Click += new System.EventHandler(this.imgMinimizar_Click);
            //
            // imgSalir
            //
            this.imgSalir.AccessibleName = "Cerrar DigLab";
            this.imgSalir.BackColor = System.Drawing.Color.Transparent;
            this.imgSalir.Cursor = System.Windows.Forms.Cursors.Hand;
            this.imgSalir.ForeColor = System.Drawing.Color.White;
            this.imgSalir.Location = new System.Drawing.Point(436, 8);
            this.imgSalir.Name = "imgSalir";
            this.imgSalir.Size = new System.Drawing.Size(36, 32);
            this.imgSalir.TabIndex = 20;
            this.imgSalir.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.imgSalir.Click += new System.EventHandler(this.imgSalir_Click);
            //
            // Login
            //
            this.AcceptButton = this.btnIniciarSesion;
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(112)))), ((int)(((byte)(64)))));
            this.ClientSize = new System.Drawing.Size(480, 640);
            this.Controls.Add(this.imgSalir);
            this.Controls.Add(this.imgMinimizar);
            this.Controls.Add(this.panelTarjeta);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "Login";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Iniciar sesión - DigLab";
            this.panelTarjeta.ResumeLayout(false);
            this.panelTarjeta.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.imgLogo)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Panel panelTarjeta;
        private System.Windows.Forms.PictureBox imgLogo;
        private System.Windows.Forms.TextBox txtUsuario;
        private System.Windows.Forms.TextBox txtPassword;
        private RJControls.RJButton btnIniciarSesion;
        private System.Windows.Forms.LinkLabel linkPasswd;
        private RJControls.RJButton btnRegistro;
        private System.Windows.Forms.Label imgMinimizar;
        private System.Windows.Forms.Label imgSalir;
    }
}
