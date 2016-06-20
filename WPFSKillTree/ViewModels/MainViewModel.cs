using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using POESKillTree.SkillTreeFiles;
using PropertyChanged;

namespace POESKillTree.ViewModels
{
    [ImplementPropertyChanged]
    public class MainViewModel
    {
        public int CharacterClassIndex { get; set; }
        public string CharacterClass { get; set; }
        public List<string> CharacterClasses { get; set; }
        public int AscendancyClassIndex { get; set; }
        public List<string> AscendancyClasses { get; set; }
        public SkillTree Tree { get; set; }
        public bool UserInteraction { get; set; }
    }
}