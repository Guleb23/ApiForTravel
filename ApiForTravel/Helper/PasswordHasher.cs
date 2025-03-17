using ApiForTravel.Models;
using Microsoft.AspNetCore.Identity;

namespace ApiForTravel.Helper
{
    public class PasswordHasher
    {
        private readonly PasswordHasher<UserModel> _passwordHasher = new PasswordHasher<UserModel>();

        // Метод для хеширования пароля
        public string HashPassword(UserModel user, string password)
        {
            return _passwordHasher.HashPassword(user, password);
        }

        // Метод для проверки пароля
        public PasswordVerificationResult VerifyPassword(UserModel user, string hashedPassword, string providedPassword)
        {
            return _passwordHasher.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
