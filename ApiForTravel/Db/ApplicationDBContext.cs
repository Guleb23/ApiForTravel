using ApiForTravel.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace ApiForTravel.Db
{
    public class ApplicationDBContext:DbContext
    {
        public ApplicationDBContext(DbContextOptions<ApplicationDBContext> options) : base(options)
        {
            
        }


        // Настройка связей в таблицах
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            
            modelBuilder.Entity<UserModel>()
            .HasMany(u => u.Travels)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);


            modelBuilder.Entity<TravelModel>()
            .HasMany(t => t.Points)
            .WithOne(p => p.Travel)
            .HasForeignKey(p => p.TravelId)
            .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TravelPoint>()
                .HasOne(p => p.Coordinates)
                .WithOne(c => c.TravelPoint)
                .HasForeignKey<Coordinates>(c => c.Id)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<TravelPoint>()
                .HasMany(p => p.Photos)
                .WithOne(ph => ph.TravelPoint)
                .HasForeignKey(ph => ph.TravelPointId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        //Создания таблиц с помощью моделей
        public DbSet<UserModel> Users { get; set; }
        public DbSet<TravelModel> Travels { get; set; }
        public DbSet<TravelPoint> TravelPoints { get; set; }
        public DbSet<Coordinates> Coordinates { get; set; }
        public DbSet<Photo> PointPhotos { get; set; }
    }
}
