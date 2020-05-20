using System;
using System.Collections.Generic;
using System.Text;

namespace FMBot.Persistence.Users
{
    public static class UserSchema
    {
        public static readonly Table { get; } = nameof(User).ToSnakeCase();

        public static class Columns
        {
            public static string Id { get; } = nameof(User.Id).ToSnakeCase();
            public static string Email { get; } = nameof(User.Email).ToSnakeCase();
            public static string FirstName { get; } = nameof(User.FirstName).ToSnakeCase();
            public static string LastName { get; } = nameof(User.LastName).ToSnakeCase();
        }
    } 
}
