using System.Collections.Generic;
using FMBot.Domain.Enums;

namespace FMBot.Bot.Models;

public static class PeriodAliases
{
    public sealed class PeriodTokens
    {
        public string[] OneDay { get; init; } = [];
        public string[] Today { get; init; } = [];
        public string[] Yesterday { get; init; } = [];
        public string[] TwoDays { get; init; } = [];
        public string[] ThreeDays { get; init; } = [];
        public string[] FourDays { get; init; } = [];
        public string[] FiveDays { get; init; } = [];
        public string[] SixDays { get; init; } = [];
        public string[] Weekly { get; init; } = [];
        public string[] Monthly { get; init; } = [];
        public string[] Quarterly { get; init; } = [];
        public string[] HalfYearly { get; init; } = [];
        public string[] Yearly { get; init; } = [];
        public string[] TwoYear { get; init; } = [];
        public string[] AllTime { get; init; } = [];
        public string[] ExcludedMonths { get; init; } = [];
    }

    private static readonly PeriodTokens None = new();

    private static readonly Dictionary<Language, PeriodTokens> Tokens = new()
    {
        [Language.German] = new PeriodTokens
        {
            OneDay = ["24stunden", "24std"],
            Today = ["heute", "täglich", "taeglich", "tag"],
            Yesterday = ["gestern"],
            TwoDays = ["2 tage"],
            ThreeDays = ["3 tage"],
            FourDays = ["4 tage"],
            FiveDays = ["5 tage"],
            SixDays = ["6 tage"],
            Weekly = ["woche", "wöchentlich", "woechentlich", "wochenweise"],
            Monthly = ["monat", "monatlich", "monatliche"],
            Quarterly = ["quartal", "vierteljahr", "vierteljährlich", "vierteljaehrlich", "quartalsweise"],
            HalfYearly = ["halbjahr", "halbjährlich", "halbjaehrlich"],
            Yearly = ["jahr", "jährlich", "jaehrlich"],
            TwoYear = ["2 jahre", "zweijährig", "zweijaehrig", "zweijahre"],
            AllTime = ["insgesamt", "gesamt", "allzeit", "gesamtzeit"]
        },
        [Language.Dutch] = new PeriodTokens
        {
            OneDay = ["24 uur", "24uur"],
            Today = ["vandaag", "dagelijks", "dag"],
            Yesterday = ["gisteren", "gister"],
            TwoDays = ["2 dagen"],
            ThreeDays = ["3 dagen"],
            FourDays = ["4 dagen"],
            FiveDays = ["5 dagen"],
            SixDays = ["6 dagen"],
            Weekly = ["wekelijks", "wekelijkse"],
            Monthly = ["maand", "maandelijks", "maandelijkse"],
            Quarterly = ["kwartaal", "driemaandelijks"],
            HalfYearly = ["half jaar", "halfjaar", "halfjaarlijks"],
            Yearly = ["jaar", "jaarlijks", "jaarlijkse"],
            TwoYear = ["2 jaar", "tweejaarlijks"],
            AllTime = ["aller tijden", "allertijden", "altijd", "totaal"]
        },
        [Language.French] = new PeriodTokens
        {
            OneDay = ["des dernières 24h"],
            Today = ["du jour", "aujourdhui", "aujourd'hui", "quotidien", "journalier"],
            Yesterday = ["d'hier", "hier"],
            TwoDays = ["des 2 derniers jours"],
            ThreeDays = ["des 3 derniers jours"],
            FourDays = ["des 4 derniers jours"],
            FiveDays = ["des 5 derniers jours"],
            SixDays = ["des 6 derniers jours"],
            Weekly = ["de la semaine", "semaine", "hebdo", "hebdomadaire"],
            Monthly = ["du mois", "mois", "mensuel", "mensuelle"],
            Quarterly = ["du trimestre", "trimestre", "trimestriel", "trimestrielle"],
            HalfYearly = ["des 6 derniers mois", "semestre", "semestriel", "semestrielle"],
            Yearly = ["de l'année", "l'année", "annee", "année", "annuel", "annuelle"],
            TwoYear = ["des 2 dernières années", "biennal", "bisannuel"],
            AllTime = ["de tous les temps", "historique"],
            ExcludedMonths = ["mars"]
        },
        [Language.Spanish] = new PeriodTokens
        {
            Today = ["diario", "hoy"],
            Yesterday = ["ayer"],
            TwoDays = ["2 días", "2 dias"],
            ThreeDays = ["3 días", "3 dias"],
            FourDays = ["4 días", "4 dias"],
            FiveDays = ["5 días", "5 dias"],
            SixDays = ["6 días", "6 dias"],
            Weekly = ["semanal", "semana", "semanalmente"],
            Monthly = ["mensual", "mes", "mensualmente"],
            Quarterly = ["trimestral", "trimestre"],
            HalfYearly = ["semestral", "semestre"],
            Yearly = ["anual", "año", "anio", "anualmente"],
            TwoYear = ["2 años", "2 anos", "bienal"],
            AllTime = ["historico", "histórico"]
        },
        [Language.Portuguese] = new PeriodTokens
        {
            OneDay = ["das últimas 24h"],
            Today = ["do dia", "hoje", "diário", "diario"],
            Yesterday = ["de ontem", "ontem"],
            TwoDays = ["dos últimos 2 dias"],
            ThreeDays = ["dos últimos 3 dias"],
            FourDays = ["dos últimos 4 dias"],
            FiveDays = ["dos últimos 5 dias"],
            SixDays = ["dos últimos 6 dias"],
            Weekly = ["da semana", "semana", "semanal", "semanalmente"],
            Monthly = ["do mês", "mensal", "mês", "mes", "mensalmente"],
            Quarterly = ["do trimestre", "trimestre", "trimestral"],
            HalfYearly = ["do semestre", "semestre", "semestral"],
            Yearly = ["do ano", "anual", "ano", "anualmente"],
            TwoYear = ["dos últimos 2 anos", "bienal"],
            AllTime = ["de todos os tempos", "geral"]
        },
        [Language.Italian] = new PeriodTokens
        {
            Today = ["giornaliero", "oggi", "quotidiano"],
            Yesterday = ["ieri"],
            TwoDays = ["2 giorni"],
            ThreeDays = ["3 giorni"],
            FourDays = ["4 giorni"],
            FiveDays = ["5 giorni"],
            SixDays = ["6 giorni"],
            Weekly = ["settimana", "settimanale"],
            Monthly = ["mese", "mensile"],
            Quarterly = ["trimestre", "trimestrale"],
            HalfYearly = ["semestre", "semestrale"],
            Yearly = ["annuale", "anno"],
            TwoYear = ["biennale"],
            AllTime = ["di sempre", "totale", "complessivo"]
        },
        [Language.Polish] = new PeriodTokens
        {
            OneDay = ["doba", "dobowy"],
            Today = ["dzisiaj", "dzienny", "dziś", "dzis", "dzień", "dzien"],
            Yesterday = ["wczoraj", "wczorajszy"],
            TwoDays = ["2 dni"],
            ThreeDays = ["3 dni"],
            FourDays = ["4 dni"],
            FiveDays = ["5 dni"],
            SixDays = ["6 dni"],
            Weekly = ["tydzień", "tydzien", "tygodniowy", "tygodniowo", "tygodnia"],
            Monthly = ["miesiąc", "miesiac", "miesięczny", "miesieczny", "miesięcznie", "miesiecznie", "miesiąca", "miesiaca"],
            Quarterly = ["kwartał", "kwartal", "kwartalny", "kwartalnie"],
            HalfYearly = ["pół roku", "pol roku", "półrocze", "polrocze", "półroczny", "polroczny", "półroku", "polroku", "półrocznie", "polrocznie"],
            Yearly = ["roczny", "rocznie", "rok", "roku"],
            TwoYear = ["2 lata", "dwuletni"],
            AllTime = ["ogółem", "ogolem", "łącznie", "lacznie"]
        },
        [Language.Swedish] = new PeriodTokens
        {
            OneDay = ["dygn"],
            Today = ["idag", "dagligen"],
            Yesterday = ["igår", "gårdagen"],
            TwoDays = ["senaste 2 dagarna"],
            ThreeDays = ["senaste 3 dagarna"],
            FourDays = ["senaste 4 dagarna"],
            FiveDays = ["senaste 5 dagarna"],
            SixDays = ["senaste 6 dagarna"],
            Weekly = ["senaste veckan", "vecka", "veckan", "veckovis"],
            Monthly = ["senaste månaden", "månad", "månaden", "månadsvis"],
            Quarterly = ["senaste kvartalet", "kvartal", "kvartalet", "kvartalsvis"],
            HalfYearly = ["senaste halvåret", "halvår", "halvåret", "halvårsvis"],
            Yearly = ["senaste året", "året", "årligen", "årsvis", "år"],
            TwoYear = ["senaste 2 åren"],
            AllTime = ["genom tiderna", "totalt", "alltid"],
            ExcludedMonths = ["mars"]
        },
        [Language.Turkish] = new PeriodTokens
        {
            OneDay = ["24 saatlik"],
            Today = ["günlük", "gunluk", "bugün", "bugun"],
            Yesterday = ["dünkü", "dunku", "dün", "dun"],
            TwoDays = ["2 günlük", "2 gunluk"],
            ThreeDays = ["3 günlük", "3 gunluk"],
            FourDays = ["4 günlük", "4 gunluk"],
            FiveDays = ["5 günlük", "5 gunluk"],
            SixDays = ["6 günlük", "6 gunluk"],
            Weekly = ["haftalık", "haftalik", "hafta"],
            Monthly = ["aylık", "aylik"],
            Quarterly = ["3 aylık", "3 aylik", "çeyrek", "ceyrek", "çeyreklik", "ceyreklik", "3aylık", "3aylik"],
            HalfYearly = ["6 aylık", "6 aylik", "altı aylık", "alti aylik", "yarıyıl", "yariyil", "altıaylık", "altiaylik", "6aylık", "6aylik"],
            Yearly = ["yıllık", "yillik", "senelik", "yıl", "yil"],
            TwoYear = ["2 yıllık", "2 yillik", "iki yıllık", "iki yillik", "2yıllık", "2yillik", "ikiyıllık", "ikiyillik"],
            AllTime = ["genel", "toplam"]
        },
        [Language.Hindi] = new PeriodTokens
        {
            OneDay = ["24 घंटे"],
            Today = ["1 दिन", "दैनिक", "dainik", "आज", "aaj"],
            Yesterday = ["कल"],
            TwoDays = ["2 दिन"],
            ThreeDays = ["3 दिन"],
            FourDays = ["4 दिन"],
            FiveDays = ["5 दिन"],
            SixDays = ["6 दिन"],
            Weekly = ["साप्ताहिक", "सप्ताह", "हफ़्ता", "हफ्ता", "हफ़्ते", "हफ्ते", "saptahik", "saptah", "hafta", "hafte"],
            Monthly = ["मासिक", "masik", "maasik", "महीना", "महीने", "mahina", "mahine", "maheena"],
            Quarterly = ["तिमाही", "timahi"],
            HalfYearly = ["छमाही", "chhamahi", "chamahi"],
            Yearly = ["सालाना", "वार्षिक", "salana", "saalana", "varshik", "साल", "saal"],
            TwoYear = ["2 साल"],
            AllTime = ["ऑलटाइम", "ऑल-टाइम", "सर्वकालिक"]
        },
        [Language.Indonesian] = new PeriodTokens
        {
            OneDay = ["24 jam", "24jam"],
            Today = ["harian"],
            Yesterday = ["kemarin"],
            TwoDays = ["2 hari"],
            ThreeDays = ["3 hari"],
            FourDays = ["4 hari"],
            FiveDays = ["5 hari"],
            SixDays = ["6 hari"],
            Weekly = ["mingguan", "seminggu", "minggu"],
            Monthly = ["bulanan", "sebulan", "bulan"],
            Quarterly = ["3 bulan", "kuartal", "kuartalan", "triwulan", "triwulanan"],
            HalfYearly = ["6 bulan", "semester", "semesteran"],
            Yearly = ["tahunan", "setahun", "tahun"],
            TwoYear = ["2 tahun"],
            AllTime = ["sepanjang masa", "keseluruhan"]
        }
    };

    public static PeriodTokens For(Language language)
    {
        return Tokens.GetValueOrDefault(language, None);
    }
}
