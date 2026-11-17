using System.Runtime.Serialization;

namespace Blueprint.Localization
{
    /// <summary>
    /// The countries and territories of ISO 3166-1.
    /// </summary>
    /// <remarks>
    /// EnumMember contains the ISO 3166-1 alpha-2 code, and the numeric value is the official numeric-3 code.
    /// Each entry's documentation includes its alpha-3 code. The written numeric-3 code has three digits (004), while the enum value is an integer (4).
    /// Includes 249 entries, with the source lists cross-checked on 2026-11-07.
    /// Sources: <see href="https://www.iso.org/iso-3166-country-codes.html">ISO 3166</see>,
    /// <see href="https://unstats.un.org/unsd/methodology/m49/">UN Statistics Division M49</see> and
    /// <see href="https://www.ripe.net/community/internet-governance/internet-technical-community/the-rir-system/list-of-country-codes-and-rirs/">RIPE NCC</see>.
    /// </remarks>
    [DataContract]
    public enum ECountry
    {
        /// <summary>Country or territory: Afghanistan. ISO 3166-1: AF / AFG / 004.</summary>
        [EnumMember(Value = "AF")]
        Afghanistan = 4,

        /// <summary>Country or territory: Åland Islands. ISO 3166-1: AX / ALA / 248.</summary>
        [EnumMember(Value = "AX")]
        AlandIslands = 248,

        /// <summary>Country or territory: Albania. ISO 3166-1: AL / ALB / 008.</summary>
        [EnumMember(Value = "AL")]
        Albania = 8,

        /// <summary>Country or territory: Algeria. ISO 3166-1: DZ / DZA / 012.</summary>
        [EnumMember(Value = "DZ")]
        Algeria = 12,

        /// <summary>Country or territory: American Samoa. ISO 3166-1: AS / ASM / 016.</summary>
        [EnumMember(Value = "AS")]
        AmericanSamoa = 16,

        /// <summary>Country or territory: Andorra. ISO 3166-1: AD / AND / 020.</summary>
        [EnumMember(Value = "AD")]
        Andorra = 20,

        /// <summary>Country or territory: Angola. ISO 3166-1: AO / AGO / 024.</summary>
        [EnumMember(Value = "AO")]
        Angola = 24,

        /// <summary>Country or territory: Anguilla. ISO 3166-1: AI / AIA / 660.</summary>
        [EnumMember(Value = "AI")]
        Anguilla = 660,

        /// <summary>Country or territory: Antarctica. ISO 3166-1: AQ / ATA / 010.</summary>
        [EnumMember(Value = "AQ")]
        Antarctica = 10,

        /// <summary>Country or territory: Antigua and Barbuda. ISO 3166-1: AG / ATG / 028.</summary>
        [EnumMember(Value = "AG")]
        AntiguaAndBarbuda = 28,

        /// <summary>Country or territory: Argentina. ISO 3166-1: AR / ARG / 032.</summary>
        [EnumMember(Value = "AR")]
        Argentina = 32,

        /// <summary>Country or territory: Armenia. ISO 3166-1: AM / ARM / 051.</summary>
        [EnumMember(Value = "AM")]
        Armenia = 51,

        /// <summary>Country or territory: Aruba. ISO 3166-1: AW / ABW / 533.</summary>
        [EnumMember(Value = "AW")]
        Aruba = 533,

        /// <summary>Country or territory: Australia. ISO 3166-1: AU / AUS / 036.</summary>
        [EnumMember(Value = "AU")]
        Australia = 36,

        /// <summary>Country or territory: Austria. ISO 3166-1: AT / AUT / 040.</summary>
        [EnumMember(Value = "AT")]
        Austria = 40,

        /// <summary>Country or territory: Azerbaijan. ISO 3166-1: AZ / AZE / 031.</summary>
        [EnumMember(Value = "AZ")]
        Azerbaijan = 31,

        /// <summary>Country or territory: Bahamas. ISO 3166-1: BS / BHS / 044.</summary>
        [EnumMember(Value = "BS")]
        Bahamas = 44,

        /// <summary>Country or territory: Bahrain. ISO 3166-1: BH / BHR / 048.</summary>
        [EnumMember(Value = "BH")]
        Bahrain = 48,

        /// <summary>Country or territory: Bangladesh. ISO 3166-1: BD / BGD / 050.</summary>
        [EnumMember(Value = "BD")]
        Bangladesh = 50,

        /// <summary>Country or territory: Barbados. ISO 3166-1: BB / BRB / 052.</summary>
        [EnumMember(Value = "BB")]
        Barbados = 52,

        /// <summary>Country or territory: Belarus. ISO 3166-1: BY / BLR / 112.</summary>
        [EnumMember(Value = "BY")]
        Belarus = 112,

        /// <summary>Country or territory: Belgium. ISO 3166-1: BE / BEL / 056.</summary>
        [EnumMember(Value = "BE")]
        Belgium = 56,

        /// <summary>Country or territory: Belize. ISO 3166-1: BZ / BLZ / 084.</summary>
        [EnumMember(Value = "BZ")]
        Belize = 84,

        /// <summary>Country or territory: Benin. ISO 3166-1: BJ / BEN / 204.</summary>
        [EnumMember(Value = "BJ")]
        Benin = 204,

        /// <summary>Country or territory: Bermuda. ISO 3166-1: BM / BMU / 060.</summary>
        [EnumMember(Value = "BM")]
        Bermuda = 60,

        /// <summary>Country or territory: Bhutan. ISO 3166-1: BT / BTN / 064.</summary>
        [EnumMember(Value = "BT")]
        Bhutan = 64,

        /// <summary>Country or territory: Bolivia (Plurinational State of). ISO 3166-1: BO / BOL / 068.</summary>
        [EnumMember(Value = "BO")]
        Bolivia = 68,

        /// <summary>Country or territory: Bonaire, Sint Eustatius and Saba. ISO 3166-1: BQ / BES / 535.</summary>
        [EnumMember(Value = "BQ")]
        BonaireSintEustatiusAndSaba = 535,

        /// <summary>Country or territory: Bosnia and Herzegovina. ISO 3166-1: BA / BIH / 070.</summary>
        [EnumMember(Value = "BA")]
        BosniaAndHerzegovina = 70,

        /// <summary>Country or territory: Botswana. ISO 3166-1: BW / BWA / 072.</summary>
        [EnumMember(Value = "BW")]
        Botswana = 72,

        /// <summary>Country or territory: Bouvet Island. ISO 3166-1: BV / BVT / 074.</summary>
        [EnumMember(Value = "BV")]
        BouvetIsland = 74,

        /// <summary>Country or territory: Brazil. ISO 3166-1: BR / BRA / 076.</summary>
        [EnumMember(Value = "BR")]
        Brazil = 76,

        /// <summary>Country or territory: British Indian Ocean Territory. ISO 3166-1: IO / IOT / 086.</summary>
        [EnumMember(Value = "IO")]
        BritishIndianOceanTerritory = 86,

        /// <summary>Country or territory: Brunei Darussalam. ISO 3166-1: BN / BRN / 096.</summary>
        [EnumMember(Value = "BN")]
        BruneiDarussalam = 96,

        /// <summary>Country or territory: Bulgaria. ISO 3166-1: BG / BGR / 100.</summary>
        [EnumMember(Value = "BG")]
        Bulgaria = 100,

        /// <summary>Country or territory: Burkina Faso. ISO 3166-1: BF / BFA / 854.</summary>
        [EnumMember(Value = "BF")]
        BurkinaFaso = 854,

        /// <summary>Country or territory: Burundi. ISO 3166-1: BI / BDI / 108.</summary>
        [EnumMember(Value = "BI")]
        Burundi = 108,

        /// <summary>Country or territory: Cambodia. ISO 3166-1: KH / KHM / 116.</summary>
        [EnumMember(Value = "KH")]
        Cambodia = 116,

        /// <summary>Country or territory: Cameroon. ISO 3166-1: CM / CMR / 120.</summary>
        [EnumMember(Value = "CM")]
        Cameroon = 120,

        /// <summary>Country or territory: Canada. ISO 3166-1: CA / CAN / 124.</summary>
        [EnumMember(Value = "CA")]
        Canada = 124,

        /// <summary>Country or territory: Cabo Verde. ISO 3166-1: CV / CPV / 132.</summary>
        [EnumMember(Value = "CV")]
        CapeVerde = 132,

        /// <summary>Country or territory: Cayman Islands. ISO 3166-1: KY / CYM / 136.</summary>
        [EnumMember(Value = "KY")]
        CaymanIslands = 136,

        /// <summary>Country or territory: Central African Republic. ISO 3166-1: CF / CAF / 140.</summary>
        [EnumMember(Value = "CF")]
        CentralAfricanRepublic = 140,

        /// <summary>Country or territory: Chad. ISO 3166-1: TD / TCD / 148.</summary>
        [EnumMember(Value = "TD")]
        Chad = 148,

        /// <summary>Country or territory: Chile. ISO 3166-1: CL / CHL / 152.</summary>
        [EnumMember(Value = "CL")]
        Chile = 152,

        /// <summary>Country or territory: China. ISO 3166-1: CN / CHN / 156.</summary>
        [EnumMember(Value = "CN")]
        China = 156,

        /// <summary>Country or territory: Christmas Island. ISO 3166-1: CX / CXR / 162.</summary>
        [EnumMember(Value = "CX")]
        ChristmasIsland = 162,

        /// <summary>Country or territory: Cocos (Keeling) Islands. ISO 3166-1: CC / CCK / 166.</summary>
        [EnumMember(Value = "CC")]
        CocosKeelingIslands = 166,

        /// <summary>Country or territory: Colombia. ISO 3166-1: CO / COL / 170.</summary>
        [EnumMember(Value = "CO")]
        Colombia = 170,

        /// <summary>Country or territory: Comoros. ISO 3166-1: KM / COM / 174.</summary>
        [EnumMember(Value = "KM")]
        Comoros = 174,

        /// <summary>Country or territory: Congo. ISO 3166-1: CG / COG / 178.</summary>
        [EnumMember(Value = "CG")]
        CongoBrazzaville = 178,

        /// <summary>Country or territory: Democratic Republic of the Congo. ISO 3166-1: CD / COD / 180.</summary>
        [EnumMember(Value = "CD")]
        CongoKinshasa = 180,

        /// <summary>Country or territory: Cook Islands. ISO 3166-1: CK / COK / 184.</summary>
        [EnumMember(Value = "CK")]
        CookIslands = 184,

        /// <summary>Country or territory: Costa Rica. ISO 3166-1: CR / CRI / 188.</summary>
        [EnumMember(Value = "CR")]
        CostaRica = 188,

        /// <summary>Country or territory: Côte d’Ivoire. ISO 3166-1: CI / CIV / 384.</summary>
        [EnumMember(Value = "CI")]
        CoteDIvoire = 384,

        /// <summary>Country or territory: Croatia. ISO 3166-1: HR / HRV / 191.</summary>
        [EnumMember(Value = "HR")]
        Croatia = 191,

        /// <summary>Country or territory: Cuba. ISO 3166-1: CU / CUB / 192.</summary>
        [EnumMember(Value = "CU")]
        Cuba = 192,

        /// <summary>Country or territory: Curaçao. ISO 3166-1: CW / CUW / 531.</summary>
        [EnumMember(Value = "CW")]
        Curacao = 531,

        /// <summary>Country or territory: Cyprus. ISO 3166-1: CY / CYP / 196.</summary>
        [EnumMember(Value = "CY")]
        Cyprus = 196,

        /// <summary>Country or territory: Czechia. ISO 3166-1: CZ / CZE / 203.</summary>
        [EnumMember(Value = "CZ")]
        CzechRepublic = 203,

        /// <summary>Country or territory: Denmark. ISO 3166-1: DK / DNK / 208.</summary>
        [EnumMember(Value = "DK")]
        Denmark = 208,

        /// <summary>Country or territory: Djibouti. ISO 3166-1: DJ / DJI / 262.</summary>
        [EnumMember(Value = "DJ")]
        Djibouti = 262,

        /// <summary>Country or territory: Dominica. ISO 3166-1: DM / DMA / 212.</summary>
        [EnumMember(Value = "DM")]
        Dominica = 212,

        /// <summary>Country or territory: Dominican Republic. ISO 3166-1: DO / DOM / 214.</summary>
        [EnumMember(Value = "DO")]
        DominicanRepublic = 214,

        /// <summary>Country or territory: Ecuador. ISO 3166-1: EC / ECU / 218.</summary>
        [EnumMember(Value = "EC")]
        Ecuador = 218,

        /// <summary>Country or territory: Egypt. ISO 3166-1: EG / EGY / 818.</summary>
        [EnumMember(Value = "EG")]
        Egypt = 818,

        /// <summary>Country or territory: El Salvador. ISO 3166-1: SV / SLV / 222.</summary>
        [EnumMember(Value = "SV")]
        ElSalvador = 222,

        /// <summary>Country or territory: Equatorial Guinea. ISO 3166-1: GQ / GNQ / 226.</summary>
        [EnumMember(Value = "GQ")]
        EquatorialGuinea = 226,

        /// <summary>Country or territory: Eritrea. ISO 3166-1: ER / ERI / 232.</summary>
        [EnumMember(Value = "ER")]
        Eritrea = 232,

        /// <summary>Country or territory: Estonia. ISO 3166-1: EE / EST / 233.</summary>
        [EnumMember(Value = "EE")]
        Estonia = 233,

        /// <summary>Country or territory: Ethiopia. ISO 3166-1: ET / ETH / 231.</summary>
        [EnumMember(Value = "ET")]
        Ethiopia = 231,

        /// <summary>Country or territory: Falkland Islands (Malvinas). ISO 3166-1: FK / FLK / 238.</summary>
        [EnumMember(Value = "FK")]
        FalklandIslandsMalvinas = 238,

        /// <summary>Country or territory: Faroe Islands. ISO 3166-1: FO / FRO / 234.</summary>
        [EnumMember(Value = "FO")]
        FaroeIslands = 234,

        /// <summary>Country or territory: Fiji. ISO 3166-1: FJ / FJI / 242.</summary>
        [EnumMember(Value = "FJ")]
        Fiji = 242,

        /// <summary>Country or territory: Finland. ISO 3166-1: FI / FIN / 246.</summary>
        [EnumMember(Value = "FI")]
        Finland = 246,

        /// <summary>Country or territory: France. ISO 3166-1: FR / FRA / 250.</summary>
        [EnumMember(Value = "FR")]
        France = 250,

        /// <summary>Country or territory: French Guiana. ISO 3166-1: GF / GUF / 254.</summary>
        [EnumMember(Value = "GF")]
        FrenchGuiana = 254,

        /// <summary>Country or territory: French Polynesia. ISO 3166-1: PF / PYF / 258.</summary>
        [EnumMember(Value = "PF")]
        FrenchPolynesia = 258,

        /// <summary>Country or territory: French Southern Territories. ISO 3166-1: TF / ATF / 260.</summary>
        [EnumMember(Value = "TF")]
        FrenchSouthernTerritories = 260,

        /// <summary>Country or territory: Gabon. ISO 3166-1: GA / GAB / 266.</summary>
        [EnumMember(Value = "GA")]
        Gabon = 266,

        /// <summary>Country or territory: Gambia. ISO 3166-1: GM / GMB / 270.</summary>
        [EnumMember(Value = "GM")]
        Gambia = 270,

        /// <summary>Country or territory: Georgia. ISO 3166-1: GE / GEO / 268.</summary>
        [EnumMember(Value = "GE")]
        Georgia = 268,

        /// <summary>Country or territory: Germany. ISO 3166-1: DE / DEU / 276.</summary>
        [EnumMember(Value = "DE")]
        Germany = 276,

        /// <summary>Country or territory: Ghana. ISO 3166-1: GH / GHA / 288.</summary>
        [EnumMember(Value = "GH")]
        Ghana = 288,

        /// <summary>Country or territory: Gibraltar. ISO 3166-1: GI / GIB / 292.</summary>
        [EnumMember(Value = "GI")]
        Gibraltar = 292,

        /// <summary>Country or territory: Greece. ISO 3166-1: GR / GRC / 300.</summary>
        [EnumMember(Value = "GR")]
        Greece = 300,

        /// <summary>Country or territory: Greenland. ISO 3166-1: GL / GRL / 304.</summary>
        [EnumMember(Value = "GL")]
        Greenland = 304,

        /// <summary>Country or territory: Grenada. ISO 3166-1: GD / GRD / 308.</summary>
        [EnumMember(Value = "GD")]
        Grenada = 308,

        /// <summary>Country or territory: Guadeloupe. ISO 3166-1: GP / GLP / 312.</summary>
        [EnumMember(Value = "GP")]
        Guadeloupe = 312,

        /// <summary>Country or territory: Guam. ISO 3166-1: GU / GUM / 316.</summary>
        [EnumMember(Value = "GU")]
        Guam = 316,

        /// <summary>Country or territory: Guatemala. ISO 3166-1: GT / GTM / 320.</summary>
        [EnumMember(Value = "GT")]
        Guatemala = 320,

        /// <summary>Country or territory: Guernsey. ISO 3166-1: GG / GGY / 831.</summary>
        [EnumMember(Value = "GG")]
        Guernsey = 831,

        /// <summary>Country or territory: Guinea. ISO 3166-1: GN / GIN / 324.</summary>
        [EnumMember(Value = "GN")]
        Guinea = 324,

        /// <summary>Country or territory: Guinea-Bissau. ISO 3166-1: GW / GNB / 624.</summary>
        [EnumMember(Value = "GW")]
        GuineaBissau = 624,

        /// <summary>Country or territory: Guyana. ISO 3166-1: GY / GUY / 328.</summary>
        [EnumMember(Value = "GY")]
        Guyana = 328,

        /// <summary>Country or territory: Haiti. ISO 3166-1: HT / HTI / 332.</summary>
        [EnumMember(Value = "HT")]
        Haiti = 332,

        /// <summary>Country or territory: Heard Island and McDonald Islands. ISO 3166-1: HM / HMD / 334.</summary>
        [EnumMember(Value = "HM")]
        HeardAndMcDonaldIslands = 334,

        /// <summary>Country or territory: Holy See. ISO 3166-1: VA / VAT / 336.</summary>
        [EnumMember(Value = "VA")]
        VaticanCity = 336,

        /// <summary>Country or territory: Honduras. ISO 3166-1: HN / HND / 340.</summary>
        [EnumMember(Value = "HN")]
        Honduras = 340,

        /// <summary>Country or territory: China, Hong Kong Special Administrative Region. ISO 3166-1: HK / HKG / 344.</summary>
        [EnumMember(Value = "HK")]
        HongKongSARChina = 344,

        /// <summary>Country or territory: Hungary. ISO 3166-1: HU / HUN / 348.</summary>
        [EnumMember(Value = "HU")]
        Hungary = 348,

        /// <summary>Country or territory: Iceland. ISO 3166-1: IS / ISL / 352.</summary>
        [EnumMember(Value = "IS")]
        Iceland = 352,

        /// <summary>Country or territory: India. ISO 3166-1: IN / IND / 356.</summary>
        [EnumMember(Value = "IN")]
        India = 356,

        /// <summary>Country or territory: Indonesia. ISO 3166-1: ID / IDN / 360.</summary>
        [EnumMember(Value = "ID")]
        Indonesia = 360,

        /// <summary>Country or territory: Iran (Islamic Republic of). ISO 3166-1: IR / IRN / 364.</summary>
        [EnumMember(Value = "IR")]
        Iran = 364,

        /// <summary>Country or territory: Iraq. ISO 3166-1: IQ / IRQ / 368.</summary>
        [EnumMember(Value = "IQ")]
        Iraq = 368,

        /// <summary>Country or territory: Ireland. ISO 3166-1: IE / IRL / 372.</summary>
        [EnumMember(Value = "IE")]
        Ireland = 372,

        /// <summary>Country or territory: Isle of Man. ISO 3166-1: IM / IMN / 833.</summary>
        [EnumMember(Value = "IM")]
        IsleOfMan = 833,

        /// <summary>Country or territory: Israel. ISO 3166-1: IL / ISR / 376.</summary>
        [EnumMember(Value = "IL")]
        Israel = 376,

        /// <summary>Country or territory: Italy. ISO 3166-1: IT / ITA / 380.</summary>
        [EnumMember(Value = "IT")]
        Italy = 380,

        /// <summary>Country or territory: Jamaica. ISO 3166-1: JM / JAM / 388.</summary>
        [EnumMember(Value = "JM")]
        Jamaica = 388,

        /// <summary>Country or territory: Japan. ISO 3166-1: JP / JPN / 392.</summary>
        [EnumMember(Value = "JP")]
        Japan = 392,

        /// <summary>Country or territory: Jersey. ISO 3166-1: JE / JEY / 832.</summary>
        [EnumMember(Value = "JE")]
        Jersey = 832,

        /// <summary>Country or territory: Jordan. ISO 3166-1: JO / JOR / 400.</summary>
        [EnumMember(Value = "JO")]
        Jordan = 400,

        /// <summary>Country or territory: Kazakhstan. ISO 3166-1: KZ / KAZ / 398.</summary>
        [EnumMember(Value = "KZ")]
        Kazakhstan = 398,

        /// <summary>Country or territory: Kenya. ISO 3166-1: KE / KEN / 404.</summary>
        [EnumMember(Value = "KE")]
        Kenya = 404,

        /// <summary>Country or territory: Kiribati. ISO 3166-1: KI / KIR / 296.</summary>
        [EnumMember(Value = "KI")]
        Kiribati = 296,

        /// <summary>Country or territory: Democratic People&apos;s Republic of Korea. ISO 3166-1: KP / PRK / 408.</summary>
        [EnumMember(Value = "KP")]
        NorthKorea = 408,

        /// <summary>Country or territory: Republic of Korea. ISO 3166-1: KR / KOR / 410.</summary>
        [EnumMember(Value = "KR")]
        SouthKorea = 410,

        /// <summary>Country or territory: Kuwait. ISO 3166-1: KW / KWT / 414.</summary>
        [EnumMember(Value = "KW")]
        Kuwait = 414,

        /// <summary>Country or territory: Kyrgyzstan. ISO 3166-1: KG / KGZ / 417.</summary>
        [EnumMember(Value = "KG")]
        Kyrgyzstan = 417,

        /// <summary>Country or territory: Lao People&apos;s Democratic Republic. ISO 3166-1: LA / LAO / 418.</summary>
        [EnumMember(Value = "LA")]
        Laos = 418,

        /// <summary>Country or territory: Latvia. ISO 3166-1: LV / LVA / 428.</summary>
        [EnumMember(Value = "LV")]
        Latvia = 428,

        /// <summary>Country or territory: Lebanon. ISO 3166-1: LB / LBN / 422.</summary>
        [EnumMember(Value = "LB")]
        Lebanon = 422,

        /// <summary>Country or territory: Lesotho. ISO 3166-1: LS / LSO / 426.</summary>
        [EnumMember(Value = "LS")]
        Lesotho = 426,

        /// <summary>Country or territory: Liberia. ISO 3166-1: LR / LBR / 430.</summary>
        [EnumMember(Value = "LR")]
        Liberia = 430,

        /// <summary>Country or territory: Libya. ISO 3166-1: LY / LBY / 434.</summary>
        [EnumMember(Value = "LY")]
        Libya = 434,

        /// <summary>Country or territory: Liechtenstein. ISO 3166-1: LI / LIE / 438.</summary>
        [EnumMember(Value = "LI")]
        Liechtenstein = 438,

        /// <summary>Country or territory: Lithuania. ISO 3166-1: LT / LTU / 440.</summary>
        [EnumMember(Value = "LT")]
        Lithuania = 440,

        /// <summary>Country or territory: Luxembourg. ISO 3166-1: LU / LUX / 442.</summary>
        [EnumMember(Value = "LU")]
        Luxembourg = 442,

        /// <summary>Country or territory: China, Macao Special Administrative Region. ISO 3166-1: MO / MAC / 446.</summary>
        [EnumMember(Value = "MO")]
        MacaoSARChina = 446,

        /// <summary>Country or territory: Madagascar. ISO 3166-1: MG / MDG / 450.</summary>
        [EnumMember(Value = "MG")]
        Madagascar = 450,

        /// <summary>Country or territory: Malawi. ISO 3166-1: MW / MWI / 454.</summary>
        [EnumMember(Value = "MW")]
        Malawi = 454,

        /// <summary>Country or territory: Malaysia. ISO 3166-1: MY / MYS / 458.</summary>
        [EnumMember(Value = "MY")]
        Malaysia = 458,

        /// <summary>Country or territory: Maldives. ISO 3166-1: MV / MDV / 462.</summary>
        [EnumMember(Value = "MV")]
        Maldives = 462,

        /// <summary>Country or territory: Mali. ISO 3166-1: ML / MLI / 466.</summary>
        [EnumMember(Value = "ML")]
        Mali = 466,

        /// <summary>Country or territory: Malta. ISO 3166-1: MT / MLT / 470.</summary>
        [EnumMember(Value = "MT")]
        Malta = 470,

        /// <summary>Country or territory: Marshall Islands. ISO 3166-1: MH / MHL / 584.</summary>
        [EnumMember(Value = "MH")]
        MarshallIslands = 584,

        /// <summary>Country or territory: Martinique. ISO 3166-1: MQ / MTQ / 474.</summary>
        [EnumMember(Value = "MQ")]
        Martinique = 474,

        /// <summary>Country or territory: Mauritania. ISO 3166-1: MR / MRT / 478.</summary>
        [EnumMember(Value = "MR")]
        Mauritania = 478,

        /// <summary>Country or territory: Mauritius. ISO 3166-1: MU / MUS / 480.</summary>
        [EnumMember(Value = "MU")]
        Mauritius = 480,

        /// <summary>Country or territory: Mayotte. ISO 3166-1: YT / MYT / 175.</summary>
        [EnumMember(Value = "YT")]
        Mayotte = 175,

        /// <summary>Country or territory: Mexico. ISO 3166-1: MX / MEX / 484.</summary>
        [EnumMember(Value = "MX")]
        Mexico = 484,

        /// <summary>Country or territory: Micronesia (Federated States of). ISO 3166-1: FM / FSM / 583.</summary>
        [EnumMember(Value = "FM")]
        Micronesia = 583,

        /// <summary>Country or territory: Republic of Moldova. ISO 3166-1: MD / MDA / 498.</summary>
        [EnumMember(Value = "MD")]
        Moldova = 498,

        /// <summary>Country or territory: Monaco. ISO 3166-1: MC / MCO / 492.</summary>
        [EnumMember(Value = "MC")]
        Monaco = 492,

        /// <summary>Country or territory: Mongolia. ISO 3166-1: MN / MNG / 496.</summary>
        [EnumMember(Value = "MN")]
        Mongolia = 496,

        /// <summary>Country or territory: Montenegro. ISO 3166-1: ME / MNE / 499.</summary>
        [EnumMember(Value = "ME")]
        Montenegro = 499,

        /// <summary>Country or territory: Montserrat. ISO 3166-1: MS / MSR / 500.</summary>
        [EnumMember(Value = "MS")]
        Montserrat = 500,

        /// <summary>Country or territory: Morocco. ISO 3166-1: MA / MAR / 504.</summary>
        [EnumMember(Value = "MA")]
        Morocco = 504,

        /// <summary>Country or territory: Mozambique. ISO 3166-1: MZ / MOZ / 508.</summary>
        [EnumMember(Value = "MZ")]
        Mozambique = 508,

        /// <summary>Country or territory: Myanmar. ISO 3166-1: MM / MMR / 104.</summary>
        [EnumMember(Value = "MM")]
        MyanmarBurma = 104,

        /// <summary>Country or territory: Namibia. ISO 3166-1: NA / NAM / 516.</summary>
        [EnumMember(Value = "NA")]
        Namibia = 516,

        /// <summary>Country or territory: Naoero. ISO 3166-1: NR / NRU / 520.</summary>
        [EnumMember(Value = "NR")]
        Nauru = 520,

        /// <summary>Country or territory: Nepal. ISO 3166-1: NP / NPL / 524.</summary>
        [EnumMember(Value = "NP")]
        Nepal = 524,

        /// <summary>Country or territory: Netherlands (Kingdom of the). ISO 3166-1: NL / NLD / 528.</summary>
        [EnumMember(Value = "NL")]
        Netherlands = 528,

        /// <summary>Country or territory: New Caledonia. ISO 3166-1: NC / NCL / 540.</summary>
        [EnumMember(Value = "NC")]
        NewCaledonia = 540,

        /// <summary>Country or territory: New Zealand. ISO 3166-1: NZ / NZL / 554.</summary>
        [EnumMember(Value = "NZ")]
        NewZealand = 554,

        /// <summary>Country or territory: Nicaragua. ISO 3166-1: NI / NIC / 558.</summary>
        [EnumMember(Value = "NI")]
        Nicaragua = 558,

        /// <summary>Country or territory: Niger. ISO 3166-1: NE / NER / 562.</summary>
        [EnumMember(Value = "NE")]
        Niger = 562,

        /// <summary>Country or territory: Nigeria. ISO 3166-1: NG / NGA / 566.</summary>
        [EnumMember(Value = "NG")]
        Nigeria = 566,

        /// <summary>Country or territory: Niue. ISO 3166-1: NU / NIU / 570.</summary>
        [EnumMember(Value = "NU")]
        Niue = 570,

        /// <summary>Country or territory: Norfolk Island. ISO 3166-1: NF / NFK / 574.</summary>
        [EnumMember(Value = "NF")]
        NorfolkIsland = 574,

        /// <summary>Country or territory: North Macedonia. ISO 3166-1: MK / MKD / 807.</summary>
        [EnumMember(Value = "MK")]
        NorthMacedonia = 807,

        /// <summary>Country or territory: Northern Mariana Islands. ISO 3166-1: MP / MNP / 580.</summary>
        [EnumMember(Value = "MP")]
        NorthernMarianaIslands = 580,

        /// <summary>Country or territory: Norway. ISO 3166-1: NO / NOR / 578.</summary>
        [EnumMember(Value = "NO")]
        Norway = 578,

        /// <summary>Country or territory: Oman. ISO 3166-1: OM / OMN / 512.</summary>
        [EnumMember(Value = "OM")]
        Oman = 512,

        /// <summary>Country or territory: Pakistan. ISO 3166-1: PK / PAK / 586.</summary>
        [EnumMember(Value = "PK")]
        Pakistan = 586,

        /// <summary>Country or territory: Palau. ISO 3166-1: PW / PLW / 585.</summary>
        [EnumMember(Value = "PW")]
        Palau = 585,

        /// <summary>Country or territory: State of Palestine. ISO 3166-1: PS / PSE / 275.</summary>
        [EnumMember(Value = "PS")]
        PalestinianTerritories = 275,

        /// <summary>Country or territory: Panama. ISO 3166-1: PA / PAN / 591.</summary>
        [EnumMember(Value = "PA")]
        Panama = 591,

        /// <summary>Country or territory: Papua New Guinea. ISO 3166-1: PG / PNG / 598.</summary>
        [EnumMember(Value = "PG")]
        PapuaNewGuinea = 598,

        /// <summary>Country or territory: Paraguay. ISO 3166-1: PY / PRY / 600.</summary>
        [EnumMember(Value = "PY")]
        Paraguay = 600,

        /// <summary>Country or territory: Peru. ISO 3166-1: PE / PER / 604.</summary>
        [EnumMember(Value = "PE")]
        Peru = 604,

        /// <summary>Country or territory: Philippines. ISO 3166-1: PH / PHL / 608.</summary>
        [EnumMember(Value = "PH")]
        Philippines = 608,

        /// <summary>Country or territory: Pitcairn. ISO 3166-1: PN / PCN / 612.</summary>
        [EnumMember(Value = "PN")]
        PitcairnIslands = 612,

        /// <summary>Country or territory: Poland. ISO 3166-1: PL / POL / 616.</summary>
        [EnumMember(Value = "PL")]
        Poland = 616,

        /// <summary>Country or territory: Portugal. ISO 3166-1: PT / PRT / 620.</summary>
        [EnumMember(Value = "PT")]
        Portugal = 620,

        /// <summary>Country or territory: Puerto Rico. ISO 3166-1: PR / PRI / 630.</summary>
        [EnumMember(Value = "PR")]
        PuertoRico = 630,

        /// <summary>Country or territory: Qatar. ISO 3166-1: QA / QAT / 634.</summary>
        [EnumMember(Value = "QA")]
        Qatar = 634,

        /// <summary>Country or territory: Réunion. ISO 3166-1: RE / REU / 638.</summary>
        [EnumMember(Value = "RE")]
        Reunion = 638,

        /// <summary>Country or territory: Romania. ISO 3166-1: RO / ROU / 642.</summary>
        [EnumMember(Value = "RO")]
        Romania = 642,

        /// <summary>Country or territory: Russian Federation. ISO 3166-1: RU / RUS / 643.</summary>
        [EnumMember(Value = "RU")]
        Russia = 643,

        /// <summary>Country or territory: Rwanda. ISO 3166-1: RW / RWA / 646.</summary>
        [EnumMember(Value = "RW")]
        Rwanda = 646,

        /// <summary>Country or territory: Saint Helena. ISO 3166-1: SH / SHN / 654.</summary>
        [EnumMember(Value = "SH")]
        SaintHelena = 654,

        /// <summary>Country or territory: Saint Barthélemy. ISO 3166-1: BL / BLM / 652.</summary>
        [EnumMember(Value = "BL")]
        SaintBarthelemy = 652,

        /// <summary>Country or territory: Saint Kitts and Nevis. ISO 3166-1: KN / KNA / 659.</summary>
        [EnumMember(Value = "KN")]
        SaintKittsAndNevis = 659,

        /// <summary>Country or territory: Saint Lucia. ISO 3166-1: LC / LCA / 662.</summary>
        [EnumMember(Value = "LC")]
        SaintLucia = 662,

        /// <summary>Country or territory: Saint Pierre and Miquelon. ISO 3166-1: PM / SPM / 666.</summary>
        [EnumMember(Value = "PM")]
        SaintPierreAndMiquelon = 666,

        /// <summary>Country or territory: Saint Martin (French Part). ISO 3166-1: MF / MAF / 663.</summary>
        [EnumMember(Value = "MF")]
        SaintMartin = 663,

        /// <summary>Country or territory: Saint Vincent and the Grenadines. ISO 3166-1: VC / VCT / 670.</summary>
        [EnumMember(Value = "VC")]
        SaintVincentAndGrenadines = 670,

        /// <summary>Country or territory: Samoa. ISO 3166-1: WS / WSM / 882.</summary>
        [EnumMember(Value = "WS")]
        Samoa = 882,

        /// <summary>Country or territory: San Marino. ISO 3166-1: SM / SMR / 674.</summary>
        [EnumMember(Value = "SM")]
        SanMarino = 674,

        /// <summary>Country or territory: Sao Tome and Principe. ISO 3166-1: ST / STP / 678.</summary>
        [EnumMember(Value = "ST")]
        SaoTomeAndPrincipe = 678,

        /// <summary>Country or territory: Saudi Arabia. ISO 3166-1: SA / SAU / 682.</summary>
        [EnumMember(Value = "SA")]
        SaudiArabia = 682,

        /// <summary>Country or territory: Senegal. ISO 3166-1: SN / SEN / 686.</summary>
        [EnumMember(Value = "SN")]
        Senegal = 686,

        /// <summary>Country or territory: Serbia. ISO 3166-1: RS / SRB / 688.</summary>
        [EnumMember(Value = "RS")]
        Serbia = 688,

        /// <summary>Country or territory: Seychelles. ISO 3166-1: SC / SYC / 690.</summary>
        [EnumMember(Value = "SC")]
        Seychelles = 690,

        /// <summary>Country or territory: Sierra Leone. ISO 3166-1: SL / SLE / 694.</summary>
        [EnumMember(Value = "SL")]
        SierraLeone = 694,

        /// <summary>Country or territory: Singapore. ISO 3166-1: SG / SGP / 702.</summary>
        [EnumMember(Value = "SG")]
        Singapore = 702,

        /// <summary>Country or territory: Sint Maarten (Dutch part). ISO 3166-1: SX / SXM / 534.</summary>
        [EnumMember(Value = "SX")]
        SintMaarten = 534,

        /// <summary>Country or territory: Slovakia. ISO 3166-1: SK / SVK / 703.</summary>
        [EnumMember(Value = "SK")]
        Slovakia = 703,

        /// <summary>Country or territory: Slovenia. ISO 3166-1: SI / SVN / 705.</summary>
        [EnumMember(Value = "SI")]
        Slovenia = 705,

        /// <summary>Country or territory: Solomon Islands. ISO 3166-1: SB / SLB / 090.</summary>
        [EnumMember(Value = "SB")]
        SolomonIslands = 90,

        /// <summary>Country or territory: Somalia. ISO 3166-1: SO / SOM / 706.</summary>
        [EnumMember(Value = "SO")]
        Somalia = 706,

        /// <summary>Country or territory: South Africa. ISO 3166-1: ZA / ZAF / 710.</summary>
        [EnumMember(Value = "ZA")]
        SouthAfrica = 710,

        /// <summary>Country or territory: South Georgia and the South Sandwich Islands. ISO 3166-1: GS / SGS / 239.</summary>
        [EnumMember(Value = "GS")]
        SouthGeorgiaAndSouthSandwichIslands = 239,

        /// <summary>Country or territory: South Sudan. ISO 3166-1: SS / SSD / 728.</summary>
        [EnumMember(Value = "SS")]
        SouthSudan = 728,

        /// <summary>Country or territory: Spain. ISO 3166-1: ES / ESP / 724.</summary>
        [EnumMember(Value = "ES")]
        Spain = 724,

        /// <summary>Country or territory: Sri Lanka. ISO 3166-1: LK / LKA / 144.</summary>
        [EnumMember(Value = "LK")]
        SriLanka = 144,

        /// <summary>Country or territory: Sudan. ISO 3166-1: SD / SDN / 729.</summary>
        [EnumMember(Value = "SD")]
        Sudan = 729,

        /// <summary>Country or territory: Suriname. ISO 3166-1: SR / SUR / 740.</summary>
        [EnumMember(Value = "SR")]
        Suriname = 740,

        /// <summary>Country or territory: Svalbard and Jan Mayen Islands. ISO 3166-1: SJ / SJM / 744.</summary>
        [EnumMember(Value = "SJ")]
        SvalbardAndJanMayen = 744,

        /// <summary>Country or territory: Eswatini. ISO 3166-1: SZ / SWZ / 748.</summary>
        [EnumMember(Value = "SZ")]
        Swaziland = 748,

        /// <summary>Country or territory: Sweden. ISO 3166-1: SE / SWE / 752.</summary>
        [EnumMember(Value = "SE")]
        Sweden = 752,

        /// <summary>Country or territory: Switzerland. ISO 3166-1: CH / CHE / 756.</summary>
        [EnumMember(Value = "CH")]
        Switzerland = 756,

        /// <summary>Country or territory: Syrian Arab Republic. ISO 3166-1: SY / SYR / 760.</summary>
        [EnumMember(Value = "SY")]
        Syria = 760,

        /// <summary>Country or territory: Taiwan (Province of China). ISO 3166-1: TW / TWN / 158.</summary>
        [EnumMember(Value = "TW")]
        Taiwan = 158,

        /// <summary>Country or territory: Tajikistan. ISO 3166-1: TJ / TJK / 762.</summary>
        [EnumMember(Value = "TJ")]
        Tajikistan = 762,

        /// <summary>Country or territory: United Republic of Tanzania. ISO 3166-1: TZ / TZA / 834.</summary>
        [EnumMember(Value = "TZ")]
        Tanzania = 834,

        /// <summary>Country or territory: Thailand. ISO 3166-1: TH / THA / 764.</summary>
        [EnumMember(Value = "TH")]
        Thailand = 764,

        /// <summary>Country or territory: Timor-Leste. ISO 3166-1: TL / TLS / 626.</summary>
        [EnumMember(Value = "TL")]
        TimorLeste = 626,

        /// <summary>Country or territory: Togo. ISO 3166-1: TG / TGO / 768.</summary>
        [EnumMember(Value = "TG")]
        Togo = 768,

        /// <summary>Country or territory: Tokelau. ISO 3166-1: TK / TKL / 772.</summary>
        [EnumMember(Value = "TK")]
        Tokelau = 772,

        /// <summary>Country or territory: Tonga. ISO 3166-1: TO / TON / 776.</summary>
        [EnumMember(Value = "TO")]
        Tonga = 776,

        /// <summary>Country or territory: Trinidad and Tobago. ISO 3166-1: TT / TTO / 780.</summary>
        [EnumMember(Value = "TT")]
        TrinidadAndTobago = 780,

        /// <summary>Country or territory: Tunisia. ISO 3166-1: TN / TUN / 788.</summary>
        [EnumMember(Value = "TN")]
        Tunisia = 788,

        /// <summary>Country or territory: Türkiye. ISO 3166-1: TR / TUR / 792.</summary>
        [EnumMember(Value = "TR")]
        Turkey = 792,

        /// <summary>Country or territory: Turkmenistan. ISO 3166-1: TM / TKM / 795.</summary>
        [EnumMember(Value = "TM")]
        Turkmenistan = 795,

        /// <summary>Country or territory: Turks and Caicos Islands. ISO 3166-1: TC / TCA / 796.</summary>
        [EnumMember(Value = "TC")]
        TurksAndCaicosIslands = 796,

        /// <summary>Country or territory: Tuvalu. ISO 3166-1: TV / TUV / 798.</summary>
        [EnumMember(Value = "TV")]
        Tuvalu = 798,

        /// <summary>Country or territory: Uganda. ISO 3166-1: UG / UGA / 800.</summary>
        [EnumMember(Value = "UG")]
        Uganda = 800,

        /// <summary>Country or territory: Ukraine. ISO 3166-1: UA / UKR / 804.</summary>
        [EnumMember(Value = "UA")]
        Ukraine = 804,

        /// <summary>Country or territory: United Arab Emirates. ISO 3166-1: AE / ARE / 784.</summary>
        [EnumMember(Value = "AE")]
        UnitedArabEmirates = 784,

        /// <summary>Country or territory: United Kingdom of Great Britain and Northern Ireland. ISO 3166-1: GB / GBR / 826.</summary>
        [EnumMember(Value = "GB")]
        UnitedKingdom = 826,

        /// <summary>Country or territory: United States of America. ISO 3166-1: US / USA / 840.</summary>
        [EnumMember(Value = "US")]
        UnitedStatesOfAmerica = 840,

        /// <summary>Country or territory: United States Minor Outlying Islands. ISO 3166-1: UM / UMI / 581.</summary>
        [EnumMember(Value = "UM")]
        UnitedStatesMinorOutlyingIslands = 581,

        /// <summary>Country or territory: Uruguay. ISO 3166-1: UY / URY / 858.</summary>
        [EnumMember(Value = "UY")]
        Uruguay = 858,

        /// <summary>Country or territory: Uzbekistan. ISO 3166-1: UZ / UZB / 860.</summary>
        [EnumMember(Value = "UZ")]
        Uzbekistan = 860,

        /// <summary>Country or territory: Vanuatu. ISO 3166-1: VU / VUT / 548.</summary>
        [EnumMember(Value = "VU")]
        Vanuatu = 548,

        /// <summary>Country or territory: Venezuela (Bolivarian Republic of). ISO 3166-1: VE / VEN / 862.</summary>
        [EnumMember(Value = "VE")]
        Venezuela = 862,

        /// <summary>Country or territory: Viet Nam. ISO 3166-1: VN / VNM / 704.</summary>
        [EnumMember(Value = "VN")]
        Vietnam = 704,

        /// <summary>Country or territory: British Virgin Islands. ISO 3166-1: VG / VGB / 092.</summary>
        [EnumMember(Value = "VG")]
        VirginIslandsBritish = 92,

        /// <summary>Country or territory: United States Virgin Islands. ISO 3166-1: VI / VIR / 850.</summary>
        [EnumMember(Value = "VI")]
        VirginIslandsUS = 850,

        /// <summary>Country or territory: Wallis and Futuna Islands. ISO 3166-1: WF / WLF / 876.</summary>
        [EnumMember(Value = "WF")]
        WallisAndFutuna = 876,

        /// <summary>Country or territory: Western Sahara. ISO 3166-1: EH / ESH / 732.</summary>
        [EnumMember(Value = "EH")]
        WesternSahara = 732,

        /// <summary>Country or territory: Yemen. ISO 3166-1: YE / YEM / 887.</summary>
        [EnumMember(Value = "YE")]
        Yemen = 887,

        /// <summary>Country or territory: Zambia. ISO 3166-1: ZM / ZMB / 894.</summary>
        [EnumMember(Value = "ZM")]
        Zambia = 894,

        /// <summary>Country or territory: Zimbabwe. ISO 3166-1: ZW / ZWE / 716.</summary>
        [EnumMember(Value = "ZW")]
        Zimbabwe = 716
    }
}
