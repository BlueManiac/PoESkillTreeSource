﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyChanged;

namespace POESKillTree.ViewModels
{
    [ImplementPropertyChanged]
    public class MainViewModel
    {
        public int CharacterClassIndex { get; set; }
        public string CharacterClass { get; set; }
        public List<string> CharacterClasses { get; set; }
    }
}