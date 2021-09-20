using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ADB_Explorer.Models
{
    public class FileOperationQueue
    {
        public MyObservableCollection<FileOperation> Operations { get; private set; }

        public int CurrentOperationIndex { get; private set; }
        public FileOperation CurrentOperation { get; private set; }

        public bool IsActive { get; private set; }
        public bool ContinueOnFailure { get; set; }
        public bool StartOnAddition { get; set; }

        public FileOperationQueue()
        {
            CurrentOperationIndex = -1;
            ContinueOnFailure = true;
            StartOnAddition = true;
            Operations = new();
            Operations.CollectionChanged += Operations_CollectionChanged;
        }

        ~FileOperationQueue()
        {
            Operations.CollectionChanged -= Operations_CollectionChanged;
        }

        public void AddOperation(FileOperation fileOp)
        {
            Operations.Add(fileOp);
            
            if (StartOnAddition)
            {
                Start();
            }
        }

        public void Start()
        {
            if (!IsActive)
            {
                IsActive = true;

                if (CurrentOperationIndex == -1)
                {
                    CurrentOperationIndex = Operations.Count - 1;
                    CurrentOperation = Operations[CurrentOperationIndex];
                }

                CurrentOperation.Start();
            }
        }

        private void Operations_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Item changed
            if (IsActive && (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace))
            {
                if ((CurrentOperation.Status == FileOperation.OperationStatus.Failed) ||
                    (CurrentOperation.Status == FileOperation.OperationStatus.Completed))
                {
                    if (CurrentOperationIndex + 1 < Operations.Count)
                    {
                        ++CurrentOperationIndex;
                        CurrentOperation = Operations[CurrentOperationIndex];

                        if (ContinueOnFailure || (CurrentOperation.Status != FileOperation.OperationStatus.Failed))
                        {
                            CurrentOperation.Start();
                        }
                    }
                    else
                    {
                        CurrentOperationIndex = -1;
                        CurrentOperation = null;
                        IsActive = false;
                    }
                }
            }
        }
    }
}
