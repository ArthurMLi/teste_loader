using System.Windows;

namespace RevitLoader.App
{
    // OBSOLETO: Esta janela não é mais utilizada no fluxo principal do programa.
    // Mantida apenas para referência ou uso futuro, conforme solicitado.
    public partial class LoginWindow : Window
    {
        public string Email => txtEmail.Text?.Trim();
        public string Senha => txtSenha.Password;

        public LoginWindow()
        {
            InitializeComponent();
        }

        private void BtnEntrar_Click(object sender, RoutedEventArgs e)
        {
            txtStatus.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Senha))
            {
                txtStatus.Text = "Informe email e senha.";
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}