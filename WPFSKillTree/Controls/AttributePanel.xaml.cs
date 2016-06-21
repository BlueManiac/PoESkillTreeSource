using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MahApps.Metro.Controls;
using MahApps.Metro.Controls.Dialogs;
using POESKillTree.Controls.Dialogs;
using POESKillTree.Localization;
using POESKillTree.Model.Items;
using POESKillTree.SkillTreeFiles;
using POESKillTree.Utils;
using POESKillTree.Utils.Converter;
using POESKillTree.Utils.Extensions;
using POESKillTree.ViewModels;
using POESKillTree.Views;
using Attribute = POESKillTree.ViewModels.Attribute;

namespace POESKillTree.Controls
{
    /// <summary>
    /// Interaction logic for AttributePanel.xaml
    /// </summary>
    public partial class AttributePanel
    {
        private readonly Regex _backreplace = new Regex("#");


        public readonly ContextMenu AttributeContextMenu;

        private readonly MenuItem _cmAddToGroup;
        private readonly MenuItem _cmDeleteGroup;

        public AttributePanelViewModel ViewModel => DataContext as AttributePanelViewModel;

        public AttributePanel()
        {
            InitializeComponent();

            DataContext = new AttributePanelViewModel();

            var cmHighlight = new MenuItem
            {
                Header = L10n.Message("Highlight nodes by attribute")
            };
            cmHighlight.Click += HighlightNodesByAttribute;
            var cmRemoveHighlight = new MenuItem
            {
                Header = L10n.Message("Remove highlights by attribute")
            };
            cmRemoveHighlight.Click += UnhighlightNodesByAttribute;

            var cmCreateGroup = new MenuItem();
            cmCreateGroup.Header = "Create new group";
            cmCreateGroup.Click += CreateGroup;
            _cmAddToGroup = new MenuItem();
            _cmAddToGroup.Header = "Add to group...";
            _cmAddToGroup.IsEnabled = false;
            _cmDeleteGroup = new MenuItem();
            _cmDeleteGroup.Header = "Delete group...";
            _cmDeleteGroup.IsEnabled = false;
            var cmRemoveFromGroup = new MenuItem();
            cmRemoveFromGroup.Header = "Remove from group";
            cmRemoveFromGroup.Click += RemoveFromGroup;

            AttributeContextMenu = new ContextMenu();
            AttributeContextMenu.Items.Add(cmHighlight);
            AttributeContextMenu.Items.Add(cmRemoveHighlight);
            AttributeContextMenu.Items.Add(cmCreateGroup);
            AttributeContextMenu.Items.Add(_cmAddToGroup);
            AttributeContextMenu.Items.Add(_cmDeleteGroup);
            AttributeContextMenu.Items.Add(cmRemoveFromGroup);

            listBox1.ContextMenu = AttributeContextMenu;

            lbAllAttr.ContextMenu = AttributeContextMenu;
        }

        // Necessary to update the summed numbers in group names before every refresh
        public void RefreshAttributeLists()
        {
            if (GetActiveAttributeGroupList() == lbAllAttr)
            {
                ViewModel.AttributeGroups.UpdateGroupNames(ViewModel.AllAttributes);
            }
            //use passive attribute list as a default so nothing breaks if neither tab is actually active
            else
            {
                ViewModel.AttributeGroups.UpdateGroupNames(ViewModel.Attributes);
            }
            ViewModel.AttributesCollection.Refresh();
            ViewModel.AllAttributesCollection.Refresh();
        }


        //This whole region, along with most of GroupStringConverter, makes up our user-defined attribute group functionality - Sectoidfodder 02/29/16
        #region Attribute grouping helpers

        //there's probably a better way that doesn't break if tab ordering changes but I'm UI-challenged
        private ListBox GetActiveAttributeGroupList()
        {
            if (tabControl1.SelectedIndex == 2)
                return lbAllAttr;
            if (tabControl1.SelectedIndex == 0)
                return listBox1;

            return null;
        }

        public void SetCustomGroups(List<string[]> customgroups)
        {
            _cmAddToGroup.Items.Clear();
            _cmDeleteGroup.Items.Clear();

            var groupnames = new List<string>();

            foreach (var gp in customgroups)
            {
                if (!groupnames.Contains(gp[1]))
                {
                    groupnames.Add(gp[1]);
                }
            }

            _cmAddToGroup.IsEnabled = false;
            _cmDeleteGroup.IsEnabled = false;

            foreach (var name in groupnames)
            {
                var newSubMenu = new MenuItem { Header = name };
                newSubMenu.Click += AddToGroup;
                _cmAddToGroup.Items.Add(newSubMenu);
                _cmAddToGroup.IsEnabled = true;
                newSubMenu = new MenuItem { Header = name };
                newSubMenu.Click += DeleteGroup;
                _cmDeleteGroup.Items.Add(newSubMenu);
                _cmDeleteGroup.IsEnabled = true;
            }

            ViewModel.AttributeGroups.ResetGroups(customgroups);
            RefreshAttributeLists();
        }

        // Adds currently selected attributes to a new group
        private async void CreateGroup(object sender, RoutedEventArgs e)
        {
            ListBox lb = GetActiveAttributeGroupList();
            if (lb == null)
                return;

            var attributelist = new List<string>();
            foreach (object o in lb.SelectedItems)
            {
                attributelist.Add(o.ToString());
            }

            //Build and show form to enter group name
            var name = await DialogManager.ShowInputAsync((MetroWindow)Application.Current.MainWindow, L10n.Message("Create New Attribute Group"), L10n.Message("Group name"));
            if (!string.IsNullOrEmpty(name))
            {
                if (ViewModel.AttributeGroups.AttributeGroups.ContainsKey(name))
                {
                    await ((MetroWindow)Application.Current.MainWindow).ShowInfoAsync(L10n.Message("A group with that name already exists."));
                    return;
                }

                //Add submenus that add to and delete the new group
                var newSubMenu = new MenuItem { Header = name };
                newSubMenu.Click += AddToGroup;
                _cmAddToGroup.Items.Add(newSubMenu);
                _cmAddToGroup.IsEnabled = true;
                newSubMenu = new MenuItem { Header = name };
                newSubMenu.Click += DeleteGroup;
                _cmDeleteGroup.Items.Add(newSubMenu);
                _cmDeleteGroup.IsEnabled = true;

                //Back end - actually make the new group
                ViewModel.AttributeGroups.AddGroup(name, attributelist.ToArray());
                RefreshAttributeLists();
            }
        }

        // Removes currently selected attributes from their custom groups, restoring them to their default groups
        private void RemoveFromGroup(object sender, RoutedEventArgs e)
        {
            ListBox lb = GetActiveAttributeGroupList();
            if (lb == null)
                return;
            var attributelist = new List<string>();
            foreach (object o in lb.SelectedItems)
            {
                attributelist.Add(o.ToString());
            }
            if (attributelist.Count > 0)
            {
                ViewModel.AttributeGroups.RemoveFromGroup(attributelist.ToArray());
                RefreshAttributeLists();
            }
        }

        // Adds currently selected attributes to an existing custom group named by sender.Header
        private void AddToGroup(object sender, RoutedEventArgs e)
        {
            ListBox lb = GetActiveAttributeGroupList();
            if (lb == null)
                return;
            var attributelist = new List<string>();
            foreach (object o in lb.SelectedItems)
            {
                attributelist.Add(o.ToString());
            }
            if (attributelist.Count > 0)
            {
                ViewModel.AttributeGroups.AddGroup(((MenuItem)sender).Header.ToString(), attributelist.ToArray());
                RefreshAttributeLists();
            }
        }

        // Deletes the entire custom group named by sender.Header, restoring all contained attributes to their default groups
        private void DeleteGroup(object sender, RoutedEventArgs e)
        {
            // Remove submenus that work with the group
            for (int i = 0; i < _cmAddToGroup.Items.Count; i++)
            {
                if (((MenuItem)_cmAddToGroup.Items[i]).Header.ToString().ToLower().Equals(((MenuItem)sender).Header.ToString().ToLower()))
                {
                    _cmAddToGroup.Items.RemoveAt(i);
                    if (_cmAddToGroup.Items.Count == 0)
                        _cmAddToGroup.IsEnabled = false;
                    break;
                }
            }
            for (int i = 0; i < _cmDeleteGroup.Items.Count; i++)
            {
                if (((MenuItem)_cmDeleteGroup.Items[i]).Header.ToString().ToLower().Equals(((MenuItem)sender).Header.ToString().ToLower()))
                {
                    _cmDeleteGroup.Items.RemoveAt(i);
                    if (_cmDeleteGroup.Items.Count == 0)
                        _cmDeleteGroup.IsEnabled = false;
                    break;
                }
            }

            ViewModel.AttributeGroups.DeleteGroup(((MenuItem)sender).Header.ToString());
            RefreshAttributeLists();
        }

        #endregion

        public void UpdateAllAttributeList()
        {
            ViewModel.AllAttributes.Clear();

            if (App.MainViewModel.ItemAttributes == null)
                return;

            Dictionary<string, List<float>> attritemp = App.MainViewModel.Tree.SelectedAttributesWithoutImplicit;

            var itemAttris = App.MainViewModel.ItemAttributes.NonLocalMods
                .Select(m => new KeyValuePair<string, List<float>>(m.Attribute, m.Value))
                .SelectMany(SkillTree.ExpandHybridAttributes);

            foreach (var mod in itemAttris)
            {
                if (attritemp.ContainsKey(mod.Key))
                {
                    for (var i = 0; i < mod.Value.Count; i++)
                    {
                        attritemp[mod.Key][i] += mod.Value[i];
                    }
                }
                else
                {
                    attritemp[mod.Key] = new List<float>(mod.Value);
                }
            }

            foreach (var a in SkillTree.ImplicitAttributes(attritemp, App.MainViewModel.Tree.Level))
            {
                var key = SkillTree.RenameImplicitAttributes.ContainsKey(a.Key)
                    ? SkillTree.RenameImplicitAttributes[a.Key]
                    : a.Key;

                if (!attritemp.ContainsKey(key))
                {
                    attritemp[key] = new List<float>();
                }

                for (int i = 0; i < a.Value.Count; i++)
                {
                    if (attritemp.ContainsKey(key) && attritemp[key].Count > i)
                        attritemp[key][i] += a.Value[i];
                    else
                    {
                        attritemp[key].Add(a.Value[i]);
                    }
                }
            }

            foreach (var item in attritemp.Select(InsertNumbersInAttributes))
            {
                var a = new Attribute(item);
                if (!CheckIfAttributeMatchesFilter(a))
                    continue;

                ViewModel.AllAttributes.Add(a);
            }
        }

        public void UpdateAttributeList()
        {
            ViewModel.Attributes.Clear();
            var copy = App.MainViewModel.Tree.HighlightedAttributes == null ? null : new Dictionary<string, List<float>>(App.MainViewModel.Tree.HighlightedAttributes);

            foreach (var item in App.MainViewModel.Tree.SelectedAttributes)
            {
                var a = new Attribute(InsertNumbersInAttributes(item));
                if (!CheckIfAttributeMatchesFilter(a)) continue;
                if (copy != null && copy.ContainsKey(item.Key))
                {
                    var citem = copy[item.Key];
                    a.Deltas = item.Value.Zip(citem, (s, h) => s - h).ToArray();
                    copy.Remove(item.Key);
                }
                else
                {
                    a.Deltas = copy != null ? item.Value.ToArray() : item.Value.Select(v => 0f).ToArray();
                }
                ViewModel.Attributes.Add(a);
            }

            if (copy != null)
            {
                foreach (var item in copy)
                {
                    var a = new Attribute(InsertNumbersInAttributes(new KeyValuePair<string, List<float>>(item.Key, item.Value.Select(v => 0f).ToList())));
                    if (!CheckIfAttributeMatchesFilter(a)) continue;
                    a.Deltas = item.Value.Select(h => 0 - h).ToArray();
                    // if(item.Value.Count == 0)
                    a.Missing = true;
                    ViewModel.Attributes.Add(a);
                }
            }
        }

        private bool CheckIfAttributeMatchesFilter(Attribute attribute)
        {
            if (string.IsNullOrEmpty(ViewModel.FilterText))
                return true;

            if (ViewModel.FilterRegexEnabled)
            {
                var regex = new Regex(ViewModel.FilterText, RegexOptions.IgnoreCase);

                return regex.IsMatch(attribute.Text);
            }

            return attribute.Text.Contains(ViewModel.FilterText, StringComparison.InvariantCultureIgnoreCase);
        }

        public string GetAttributesOverview()
        {
            var sb = new StringBuilder();

            foreach (var at in ViewModel.Attributes)
            {
                sb.AppendLine(at.ToString());
            }

            return sb.ToString();
        }

        public string InsertNumbersInAttributes(KeyValuePair<string, List<float>> attrib)
        {
            string s = attrib.Key;
            foreach (float f in attrib.Value)
            {
                s = _backreplace.Replace(s, f + "", 1);
            }
            return s;
        }

        private void HighlightNodesByAttribute(object sender, RoutedEventArgs e)
        {
            var listBox = AttributeContextMenu.PlacementTarget as ListBox;
            if (listBox == null || !listBox.IsVisible) return;

            var newHighlightedAttribute =
                "^" + Regex.Replace(listBox.SelectedItem.ToString()
                        .Replace(@"+", @"\+")
                        .Replace(@"-", @"\-")
                        .Replace(@"%", @"\%"), @"[0-9]*\.?[0-9]+", @"[0-9]*\.?[0-9]+") + "$";
            App.MainViewModel.Tree.HighlightNodesBySearch(newHighlightedAttribute, true, NodeHighlighter.HighlightState.FromAttrib);
        }

        private void UnhighlightNodesByAttribute(object sender, RoutedEventArgs e)
        {
            App.MainViewModel.Tree.HighlightNodesBySearch("", true, NodeHighlighter.HighlightState.FromAttrib);
        }

        private void tbAttributesFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            FilterAttributeLists();
        }

        private void cbAttributesFilterRegEx_Click(object sender, RoutedEventArgs e)
        {
            FilterAttributeLists();
        }

        private void FilterAttributeLists()
        {
            if (ViewModel.FilterRegexEnabled && !string.IsNullOrEmpty(ViewModel.FilterText) && !RegexTools.IsValidRegex(ViewModel.FilterText))
                return;

            UpdateAllAttributeList();
            UpdateAttributeList();
            RefreshAttributeLists();
        }

        private void tabControl1_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (tabItem1.IsSelected || tabItem3.IsSelected)
                ViewModel.FilterVisibility = Visibility.Visible;
            else
                ViewModel.FilterVisibility = Visibility.Collapsed;
        }
    }
}