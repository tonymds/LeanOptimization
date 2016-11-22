﻿using GAF;
using GAF.Extensions;
using GAF.Operators;
using QuantConnect.Api;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Lean.Engine.RealTime;
using QuantConnect.Lean.Engine.Results;
using QuantConnect.Lean.Engine.Setup;
using QuantConnect.Lean.Engine.TransactionHandlers;
using QuantConnect.Logging;
using QuantConnect.Messaging;
using QuantConnect.Packets;
using QuantConnect.Queues;
using QuantConnect.Util;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Optimization
{

    class Program
    {
        private static readonly Random random = new Random();
        private static AppDomainSetup _ads;
        private static string _callingDomainName;
        private static string _exeAssembly;
        static StreamWriter writer;

        public static void Main(string[] args)
        {
            _ads = SetupAppDomain();
            writer = System.IO.File.AppendText("optimzer.txt");

            const double crossoverProbability = 0.65;
            const double mutationProbability = 0.08;
            const int elitismPercentage = 5;

            //create the population
            //var population = new Population(100, 44, false, false);

            var population = new Population();

            //create the chromosomes
            for (var p = 0; p < 10; p++)
            {

                var chromosome = new Chromosome();
                for (int i = 0; i < 8; i++)
                {
                    Variables v = new Variables();
                    v.Items = new Dictionary<string, object>
                    {
                        { "p1", RandomBetween(0.25, 3.25) },
                        { "p2", RandomBetween(0.25, 3.25) },
                        { "p3", RandomBetween(1, 12) },
                        { "p4", RandomBetween(1, 12) },
                        { "stop", RandomBetween(0.01, 0.05) },
                        { "take", RandomBetween(0.02, 0.08) },
                    };

                    chromosome.Genes.Add(new Gene(v));
                }

                var rnd = GAF.Threading.RandomProvider.GetThreadRandom();
                chromosome.Genes.ShuffleFast(rnd);
                population.Solutions.Add(chromosome);
            }

            //create the genetic operators 
            var elite = new Elite(elitismPercentage);

            var crossover = new Crossover(crossoverProbability, true)
            {
                CrossoverType = CrossoverType.DoublePoint
            };

            var swap = new SwapMutate(0.02);

            var mutation = new BinaryMutate(mutationProbability, true);

            //create the GA itself 
            var ga = new GeneticAlgorithm(population, CalculateFitness);

            //subscribe to the GAs Generation Complete event 
            ga.OnGenerationComplete += ga_OnGenerationComplete;
            ga.OnRunComplete += ga_OnRunComplete;

            //add the operators to the ga process pipeline 
            ga.Operators.Add(elite);
            ga.Operators.Add(crossover);
            //ga.Operators.Add(mutation);
            ga.Operators.Add(swap);

            //var cv_operator = new CustomOperator();
            //ga.Operators.Add(cv_operator);

            //run the GA 
            ga.Run(Terminate);

            writer.Close();

            Console.ReadKey();
        }

        static AppDomainSetup SetupAppDomain()
        {
            _callingDomainName = Thread.GetDomain().FriendlyName;
            //Console.WriteLine(callingDomainName);

            // Get and display the full name of the EXE assembly.
            _exeAssembly = Assembly.GetEntryAssembly().FullName;
            //Console.WriteLine(exeAssembly);

            // Construct and initialize settings for a second AppDomain.
            AppDomainSetup ads = new AppDomainSetup();
            ads.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;

            ads.DisallowBindingRedirects = false;
            ads.DisallowCodeDownload = true;
            ads.ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile;
            return ads;
        }

        static Runner CreateRunClassInAppDomain(ref AppDomain ad)
        {
            // Create the second AppDomain.
            var name = Guid.NewGuid().ToString("x");
            ad = AppDomain.CreateDomain(name, null, _ads);

            // Create an instance of MarshalbyRefType in the second AppDomain. 
            // A proxy to the object is returned.
            Runner rc = (Runner)ad.CreateInstanceAndUnwrap(_exeAssembly, typeof(Runner).FullName);

            return rc;
        }

        static void ga_OnRunComplete(object sender, GaEventArgs e)
        {
            var fittest = e.Population.GetTop(1)[0];
            foreach (var gene in fittest.Genes)
            {
                Variables v = (Variables)gene.ObjectValue;
                foreach (KeyValuePair<string, object> kvp in v.Items)
                    Output("{0}: value {1}", kvp.Key, kvp.Value.ToString());
            }
        }

        private static void ga_OnGenerationComplete(object sender, GaEventArgs e)
        {
            var fittest = e.Population.GetTop(1)[0];
            var sharpe = RunAlgorithm(fittest);
            Output("Generation: {0}, Fitness: {1}, sharpe: {2}", e.Generation, fittest.Fitness, sharpe);
        }

        public static double CalculateFitness(Chromosome chromosome)
        {
            try
            {
                var sharpe = RunAlgorithm(chromosome);
                return (sharpe + 10) / 200;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static double RunAlgorithm(Chromosome chromosome)
        {

            var sum_sharpe = 0.0;
            var i = 0;
            foreach (var gene in chromosome.Genes)
            {
                var val = (Variables)gene.ObjectValue;
                AppDomain ad = null;
                Runner rc = CreateRunClassInAppDomain(ref ad);
                string output = string.Format("Gene: {0}", i);
                foreach (KeyValuePair<string, object> kvp in val.Items)
                {
                    output += string.Format(" {1}: {2}", i, kvp.Key, kvp.Value.ToString());
                }

                var res = (double)rc.Run(val);
                sum_sharpe += res;
                AppDomain.Unload(ad);
                output += string.Format(" Sharpe ratio: {0}, Sum Sharpe ratio: {1}", res, sum_sharpe);

                Output(output);

                i++;
            }

            return sum_sharpe;
        }

        public static bool Terminate(Population population, int currentGeneration, long currentEvaluation)
        {
            bool canTerminate = currentGeneration > 16;
            return canTerminate;
        }

        private static double RandomNumberBetween(double minValue, double maxValue)
        {
            var next = random.NextDouble();

            return minValue + (next * (maxValue - minValue));
        }

        private static int RandomBetween(int minValue, int maxValue)
        {
            return random.Next(minValue, maxValue);
        }

        private static double RandomBetween(double minValue, double maxValue)
        {
            var rand = random.NextDouble() * (maxValue - minValue) + minValue;
            return System.Math.Round(rand, 3);
        }

        public static void Output(string line, params object[] format)
        {
            Output(string.Format(line, format));
        }

        public static void Output(string line)
        {
            writer.Write(DateTime.Now.ToString("u"));
            writer.Write(line);
            writer.Write(writer.NewLine);
            writer.Flush();
            Console.WriteLine(line);
        }

    }
}

