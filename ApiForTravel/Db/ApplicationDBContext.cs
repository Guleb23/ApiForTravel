using ApiForTravel.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiForTravel.Db
{
    public class ApplicationDBContext:DbContext
    {
        public ApplicationDBContext(DbContextOptions<ApplicationDBContext> options) : base(options)
        {
            
        }

        public DbSet<UserModel> Users { get; set; }
    }
}
