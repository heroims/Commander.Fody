# Commander.Fody

[Old Commander.Fody](https://github.com/DamianReeves/Commander.Fody) No longer maintained ,But it's very useful for us.

## This is an add-in for [Fody 6.2.0](https://github.com/Fody/Fody/) 

Injects ICommand properties and implementations for use in MVVM applications.

[Introduction to Fody](http://github.com/Fody/Fody/wiki/SampleUsage)



### Your Code
    public class SurveyViewModel
    {
        [OnCommandCanExecute("SubmitCommand")]
        public bool CanSubmit()
        {
            ... 
        }
        [OnCommand("SubmitCommand")]
        public void OnSubmit(){
            ...
        }        
    }

With a command implementation in assembly like:

    public class DelegateCommand : ICommand
    {
        ...

        public DelegateCommand(Action execute):this(execute, null)
        {        
        }

        public DelegateCommand(Action execute, Func<bool> canExecute)
        {
            ...
        }

        public void Execute(object parameter)
        {
            ...
        }

        public bool CanExecute(object parameter)
        {
            ...
        }    
    }

### What Gets Compiled
    public class SurveyViewModel
    {
        SurveyViewModel()
        {
            <Commander_Fody>InitializeCommands();            
        }
        public ICommand SubmitCommand {get;set;}

        public bool CanSubmit(){ 
            ...
        }
        public void OnSubmit(){
            ...
        }   

        private void <Commander_Fody>InitializeCommands()
        {
            if (this.SubmitCommand == null)
            {
                this.SubmitCommand = new DelegateCommand(new Action(this.OnSubmit), new Func<bool>(this.CanSubmit));
            }
        }
    }

## What it does 


## Icon
