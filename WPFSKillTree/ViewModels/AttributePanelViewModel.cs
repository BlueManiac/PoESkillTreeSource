using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using POESKillTree.Model.Items;
using POESKillTree.Utils.Converter;
using PropertyChanged;

namespace POESKillTree.ViewModels
{
    [ImplementPropertyChanged]
    public class AttributePanelViewModel
    {
        public ItemAttributes ItemAttributes { get; set; }
        public Visibility FilterVisibility { get; set; }

        public string FilterText { get; set; } = "";

        public bool FilterRegexEnabled { get; set; }

        public GroupStringConverter AttributeGroups { get; } = new GroupStringConverter();

        public List<Attribute> AllAttributes { get; } = new List<Attribute>();
        public List<Attribute> Attributes { get; } = new List<Attribute>();


        public ListCollectionView AllAttributesCollection { get; }
        public ListCollectionView AttributesCollection { get; }

        public AttributePanelViewModel()
        {
            AttributesCollection = new ListCollectionView(Attributes);
            AttributesCollection.GroupDescriptions.Add(new PropertyGroupDescription("Text", AttributeGroups));
            AttributesCollection.CustomSort = AttributeGroups;
            AllAttributesCollection = new ListCollectionView(AllAttributes);
            AllAttributesCollection.GroupDescriptions.Add(new PropertyGroupDescription("Text", AttributeGroups));
            AllAttributesCollection.CustomSort = AttributeGroups;
        }
    }
}
