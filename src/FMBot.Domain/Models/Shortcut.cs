using System;

namespace FMBot.Domain.Models;

public class Shortcut
{
        public int Id { get; set; }
        public string Input { get; set; }
        public string Output { get; set; }

        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
}
