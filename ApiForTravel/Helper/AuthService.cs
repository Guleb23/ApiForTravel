using ApiForTravel.Db;
using ApiForTravel.Models;
using Microsoft.AspNetCore.Identity;

namespace ApiForTravel.Helper
{
    public class AuthService
    {
        private readonly PasswordHasher _passwordHasherService;

        public AuthService(PasswordHasher passwordHasherService)
        {
            _passwordHasherService = passwordHasherService;
        }

        // Регистрация пользователя
        public UserModel RegisterUser(string email, string password, string identityKey, ApplicationDBContext _dbContext)
        {
            var user = new UserModel
            {
                
                Email = email,
            };

            // Хеширование пароля
            user.Password = _passwordHasherService.HashPassword(user, password);

           
            _dbContext.Users.Add(user);
            _dbContext.SaveChanges();

            return user;
        }

        // Аутентификация пользователя
        public bool AuthenticateUser(string email, string password, UserModel storedUser)
        {
            if (storedUser == null || storedUser.Email != email)
            {
                return false;
            }

            var result = _passwordHasherService.VerifyPassword(storedUser, storedUser.Password, password);
            return result == PasswordVerificationResult.Success;
        }
    }
}
