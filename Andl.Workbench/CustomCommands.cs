using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace Andl.Workbench {
  /// <summary>
  /// Container class to hold custom command definitions
  /// </summary>
  public class CustomCommands {
    public static RoutedCommand Exit = new RoutedCommand(
      "E_xit", typeof(string), new InputGestureCollection() { 
        new KeyGesture(Key.Escape) });
    public static RoutedCommand Help = new RoutedCommand(
      "Help", typeof(string), new InputGestureCollection() { 
        new KeyGesture(Key.F1) });
    public static RoutedCommand About = new RoutedCommand(
      "_About", typeof(string));
    public static RoutedCommand Properties = new RoutedCommand(
      "_Properties", typeof(string), new InputGestureCollection() { 
        new KeyGesture(Key.Enter, ModifierKeys.Alt) });
    public static RoutedCommand Execute = new RoutedCommand(
      "E_xecute", typeof(string), new InputGestureCollection() {
        new KeyGesture(Key.F5) });
    public static RoutedCommand ReloadCatalog = new RoutedCommand(
      "_Reload", typeof(string), new InputGestureCollection() {
        new KeyGesture(Key.F7) });
    public static RoutedCommand NewCatalog = new RoutedCommand(
      "_New", typeof(string), new InputGestureCollection() {
        new KeyGesture(Key.F7, ModifierKeys.Alt) });
    public static RoutedCommand SaveCatalog = new RoutedCommand(
      "_Save", typeof(string), new InputGestureCollection() {
        new KeyGesture(Key.F7, ModifierKeys.Shift) });
    public static RoutedCommand Web = new RoutedCommand(
      "_Web", typeof(string));
    public static RoutedCommand ExpandAll = new RoutedCommand(
      "_Expand All", typeof(string), new InputGestureCollection() {
        new KeyGesture(Key.Multiply, ModifierKeys.Control) });

    public static RoutedCommand Testing = new RoutedCommand(
      "_Testing", typeof(string), new InputGestureCollection() { 
        new KeyGesture(Key.T, ModifierKeys.Control | ModifierKeys.Shift) });

  }
}
