namespace Pifeon.App

open System
open Avalonia.Controls
open Avalonia.Controls.Templates
open Pifeon.App.ViewModels

type ViewLocator() =
    interface IDataTemplate with
        
        member this.Build(data: obj) =
            match data with
            | null -> null
            | _ ->
                let name = data.GetType().FullName.Replace("ViewModel", "View", StringComparison.Ordinal)
                let typ = Type.GetType(name)
                if isNull typ then
                    upcast TextBlock(Text = sprintf "Not Found: %s" name)
                else
                    Activator.CreateInstance(typ) :?> Control
                
        member this.Match(data: obj) =
            match data with
            | null -> false
            | _ -> data :? ViewModelBase