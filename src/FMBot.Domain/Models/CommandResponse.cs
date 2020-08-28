namespace FMBot.Domain.Models
{
    public enum CommandResponse
    {
        Ok = 1,

        Help = 2,

        WrongInput = 3,

        UsernameNotSet = 4,

        NotFound = 5,

        NotSupportedInDm = 6,

        Error = 7
    }
}
