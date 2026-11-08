namespace PetFam.Core.Pets;

/// <summary>What a breed says in each situation (one line is picked at random). Babies mangle it (see Pet.Say).</summary>
public sealed record BreedVoice(
    Loc[] Greeting,
    Loc[] Idle,
    Loc[] Happy,
    Loc[] Petted,
    Loc[] Hungry,
    Loc[] Sleepy,
    Loc[] Held,
    Loc[] Rain,
    Loc[] Snow,
    Loc[] Called);

/// <summary>
/// A breed: how it looks (the app draws it by Id), its temperament (the personality it is born with), how it lives
/// (energy, speed, appetite, sleep, weather and water), how fast it warms to you, and how it talks.
/// The traits follow what owners commonly say about each breed, exaggerated a little for a desktop pet.
/// Every text is in English and Korean.
/// </summary>
public sealed record Breed(
    string Id,
    PetSpecies Species,
    Loc Name,
    Loc Tagline,
    Loc Persona,
    float Playfulness, float Shyness, float Activity, float Homebody, float Tidiness,
    float Energy, float Speed, float Leap, float Appetite, float Sleepiness,
    float RainDislike, float SnowLove, float WaterLove,
    float BondRate,
    TravelMode DefaultTravel,
    Loc[] Names,
    BreedVoice Voice)
{
    public string Line(Func<BreedVoice, Loc[]> pick, Random rng)
    {
        var lines = pick(Voice);
        return lines.Length == 0 ? "" : lines[rng.Next(lines.Length)].Text;
    }

    private static Loc T(string en, string ko) => new(en, ko);

    // ---------------------------------------------------------------- dogs

    public static readonly Breed Pomeranian = new(
        "pomeranian", PetSpecies.Dog, T("Pomeranian", "포메라니안"), T("Fluffy chatterbox", "솜뭉치 수다쟁이"),
        T("A tiny fluffball full of confidence. Curious and with plenty to say, it barks to tell you about everything. It loves attention more than anything and trots after you everywhere.",
          "작은 몸에 자신감이 가득한 솜뭉치. 호기심이 많고 할 말도 많아서 무슨 일만 있으면 왈왈 알려 줘요. 관심받는 걸 제일 좋아하고 주인을 졸졸 따라다녀요."),
        0.75f, 0.2f, 0.75f, 0.3f, 0.3f,
        1.0f, 1.0f, 1.1f, 1.0f, 0f,
        0f, 1.0f, 0.5f, 1.0f, TravelMode.Follow,
        new[] { T("Peanut", "콩이"), T("Mochi", "초코"), T("Teddy", "몽이"), T("Biscuit", "보리"), T("Puff", "솜이") },
        new BreedVoice(
            Greeting: new[] { T("Hi! Let's play! Woof!", "안녕! 나랑 놀자! 왈!") },
            Idle: new[] { T("Woof! Something's over there!", "왈! 저기 뭐 있어!"), T("Look at me!", "나 좀 봐 봐!"), T("Sniff sniff… anything fun?", "킁킁… 뭐 재밌는 거 없나?"), T("Woof woof! Just saying hi", "왈왈! 그냥 불러 봤어") },
            Happy: new[] { T("Best day ever! Woof!", "최고야! 왈!"), T("Again, again!", "또 해 줘!") },
            Petted: new[] { T("Hehe, right there!", "헤헤, 거기 좋아!"), T("More pets please!", "더 쓰다듬어 줘!") },
            Hungry: new[] { T("Isn't it dinner time? Woof!", "밥 시간 아니야? 왈!"), T("My tummy's rumbling!", "배꼽시계 울린다!") },
            Sleepy: new[] { T("Yaaawn… sleepy", "하암… 졸려"), T("Just a little nap", "조금만 잘게") },
            Held: new[] { T("So high! Fun!", "높다! 신난다!"), T("Woof! A cuddle!", "왈! 안아 줬다!") },
            Rain: new[] { T("Rain! Got an umbrella?", "비다! 우산 있어?"), T("My fluff shrinks when it's wet…", "젖으면 솜이 쪼그라들어…") },
            Snow: new[] { T("Snow! Woof woof!", "눈이다! 멍멍!"), T("Crunch crunch!", "뽀득뽀득!") },
            Called: new[] { T("You called? Woof!", "불렀어? 왈!"), T("I'm here!", "나 여기 있어!") }));

    public static readonly Breed Maltese = new(
        "maltese", PetSpecies.Dog, T("Maltese", "말티즈"), T("Clingy little princess", "껌딱지 공주님"),
        T("A sweet shadow with a snow-white coat. Your side is its favourite place, and it sulks when left alone. Gentle and affectionate, but it hates getting wet: at the first drop it's off home.",
          "새하얀 털의 다정한 껌딱지. 주인 옆이 제일 좋아서 혼자 있으면 금세 시무룩해져요. 순하고 애교가 많지만 젖는 건 질색이라 비 오면 바로 집으로 쏙."),
        0.6f, 0.35f, 0.5f, 0.6f, 0.65f,
        1.05f, 0.9f, 0.9f, 0.9f, 0.05f,
        0.45f, 0.6f, 0.15f, 1.3f, TravelMode.Follow,
        new[] { T("Cloud", "구름이"), T("Snowy", "뭉치"), T("Pearl", "하얀이"), T("Marshmallow", "설기"), T("Milky", "우유") },
        new BreedVoice(
            Greeting: new[] { T("Hi… can I stay by your side?", "안녕… 나 계속 옆에 있어도 돼?") },
            Idle: new[] { T("Where are you going? Me too", "어디 가? 나도 갈래"), T("I'll stay right here", "옆에 있을래"), T("Bored… play with me", "심심해… 놀아 줘"), T("Look only at me", "나만 봐 줘") },
            Happy: new[] { T("Yay, yay!", "좋아좋아!"), T("Happy when we're together", "같이 있으니까 행복해") },
            Petted: new[] { T("Hehe, keep going", "헤헤, 계속 해 줘"), T("I'm melting…", "녹는다…") },
            Hungry: new[] { T("Hungry… anything yummy?", "배고파… 맛있는 거 있어?") },
            Sleepy: new[] { T("Let's nap together…", "같이 자자…"), T("Sleepy, stay with me", "졸려, 옆에 있어 줘") },
            Held: new[] { T("Cuddles are the best!", "안아 주는 거 제일 좋아!") },
            Rain: new[] { T("I don't want to get wet! Home!", "젖기 싫어! 집에 갈래!"), T("Wet fur is not cute…", "털 젖으면 미워져…") },
            Snow: new[] { T("Cold… but so white and pretty", "차가워… 그래도 하얘서 예쁘다") },
            Called: new[] { T("I was waiting!", "기다렸어!"), T("You called me? Hehe", "나 불렀지? 히히") }));

    public static readonly Breed Poodle = new(
        "poodle", PetSpecies.Dog, T("Toy Poodle", "토이 푸들"), T("Clever trickster", "영리한 재주꾼"),
        T("A curly brown genius. It copies anything it sees once, runs about and jumps high. It loves people and is a neat little thing that hardly sheds.",
          "곱슬곱슬 갈색 털의 똑똑이. 한 번 본 건 금방 따라 하고, 활발하게 뛰고 높이 점프해요. 사람을 좋아하고 털이 잘 안 빠지는 깔끔쟁이예요."),
        0.8f, 0.2f, 0.8f, 0.3f, 0.7f,
        1.0f, 1.1f, 1.35f, 1.0f, 0f,
        0.3f, 0.9f, 0.6f, 1.2f, TravelMode.Follow,
        new[] { T("Toffee", "토리"), T("Cookie", "쿠키"), T("Mocha", "모카"), T("Latte", "라떼"), T("Pudding", "푸딩") },
        new BreedVoice(
            Greeting: new[] { T("Nice to meet you! I know lots of tricks, want to see?", "반가워! 나 재주 많아, 보여 줄까?") },
            Idle: new[] { T("I know how to do this!", "이거 어떻게 하는지 알아!"), T("Want to see a jump?", "점프 보여 줄까?"), T("Anything new to learn?", "뭐 배울 거 없어?"), T("Throw the ball!", "공 던져 줘!") },
            Happy: new[] { T("Good job, right? Right?", "잘했지? 잘했지?"), T("That's me!", "역시 나야!") },
            Petted: new[] { T("Is that praise? Love it!", "칭찬이지? 좋아!"), T("Hehe", "헤헤") },
            Hungry: new[] { T("Surely I've earned a treat?", "간식 받을 만큼 잘했는데?") },
            Sleepy: new[] { T("All that thinking made me sleepy", "머리 많이 썼더니 졸려") },
            Held: new[] { T("Ooh, nice view from up here!", "오, 높은 데서 보니까 좋다!") },
            Rain: new[] { T("My curls will go flat!", "곱슬머리 다 풀리겠다!") },
            Snow: new[] { T("Snow jumping contest!", "눈밭에서 점프 대회다!") },
            Called: new[] { T("Call me and I come!", "부르면 바로 오지!"), T("I'm here!", "나 왔어!") }));

    public static readonly Breed Bichon = new(
        "bichon", PetSpecies.Dog, T("Bichon Frisé", "비숑 프리제"), T("Smiling cotton candy", "웃는 솜사탕"),
        T("Always smiling under a round cotton-candy head. A social butterfly that greets everyone, and when it's happy the \"bichon zoomies\" hit and it bounces all over the place.",
          "동그란 솜사탕 머리로 늘 웃는 얼굴. 누구에게나 반갑게 다가가는 사교왕이고, 신나면 갑자기 방방 뛰어다니는 '비숑 타임'이 와요."),
        0.85f, 0.1f, 0.75f, 0.4f, 0.5f,
        0.95f, 1.0f, 1.15f, 1.05f, 0f,
        0.3f, 1.1f, 0.4f, 1.2f, TravelMode.Follow,
        new[] { T("Cotton", "솜사탕"), T("Bubbles", "몽글이"), T("Fluffy", "뽀송이"), T("Marshmallow", "마시멜로"), T("Bell", "방울이") },
        new BreedVoice(
            Greeting: new[] { T("Hi hi! Let's be friends!", "안녕안녕! 우리 친구 하자!") },
            Idle: new[] { T("Zoomies time!", "비숑 타임이다!"), T("Hehe, I feel great!", "헤헤, 기분 좋아!"), T("What's everyone doing? Let's play!", "다들 뭐 해? 같이 놀자!"), T("Round and round~", "빙글빙글~") },
            Happy: new[] { T("So happy I could run!", "너무 좋아서 뛰고 싶어!"), T("Hihihi!", "히히히!") },
            Petted: new[] { T("Fluffy and happy!", "몽글몽글 좋아!"), T("Hehe, that tickles", "헤헤, 간지러워") },
            Hungry: new[] { T("Don't you smell something yummy?", "맛있는 냄새 안 나?") },
            Sleepy: new[] { T("Played so much, now I'm sleepy~", "실컷 놀았더니 졸려~") },
            Held: new[] { T("Hold me tight!", "꼭 안아 줘!") },
            Rain: new[] { T("The cotton candy will melt!", "솜사탕이 녹아 버려!") },
            Snow: new[] { T("It's white like me!", "나랑 똑같이 하얗다!") },
            Called: new[] { T("Here I come!", "왔다아!"), T("Were you looking for me?", "나 찾았어?") }));

    public static readonly Breed Shiba = new(
        "shiba", PetSpecies.Dog, T("Shiba Inu", "시바견"), T("Proud and stubborn", "도도한 고집쟁이"),
        T("An independent soul with a fox-like face. Clean and stubborn, it says no to what it doesn't like, to the very end. It screams at being picked up, but is quietly sweet with its own person.",
          "여우 같은 얼굴의 독립파. 깔끔하고 고집이 세서 싫은 건 끝까지 싫다고 해요. 안기는 건 질색이라 비명을 지르지만, 주인에게만은 은근히 다정해요."),
        0.5f, 0.5f, 0.6f, 0.5f, 0.9f,
        1.0f, 1.05f, 1.1f, 1.0f, 0f,
        0.35f, 1.2f, 0.1f, 0.75f, TravelMode.Free,
        new[] { T("Shiro", "시로"), T("Haru", "하루"), T("Maru", "마루"), T("Kuma", "코코"), T("Yuzu", "유자") },
        new BreedVoice(
            Greeting: new[] { T("…Hmph. Nice to meet you, I suppose", "…흥. 잘 부탁해, 일단은") },
            Idle: new[] { T("Hmph.", "흥."), T("I'll do as I please", "내 마음대로 할 거야"), T("…Don't follow me", "…따라오지 마"), T("I decide the walk", "산책은 내가 정해") },
            Happy: new[] { T("…Not bad", "…나쁘지 않네"), T("Hmph, it's kind of nice", "흥, 좀 좋긴 하다") },
            Petted: new[] { T("…Just a little", "…조금만이야"), T("S-stop… no, a bit more", "그, 그만… 아니 조금 더") },
            Hungry: new[] { T("Food. Now.", "밥. 지금.") },
            Sleepy: new[] { T("Don't touch me, I'm sleeping", "건드리지 마, 잘 거야") },
            Held: new[] { T("AAAAAAH!", "끼야아아앙!"), T("Put me down! Now!", "내려 줘! 당장!") },
            Rain: new[] { T("Hate rain. Not going out.", "비 싫어. 안 나가.") },
            Snow: new[] { T("…Snow is kind of nice", "…눈은 좀 좋아") },
            Called: new[] { T("…What. I'm here.", "…왜. 왔잖아."), T("Hmph, only because you called", "흥, 부르니까 온 거야") }));

    public static readonly Breed Corgi = new(
        "corgi", PetSpecies.Dog, T("Welsh Corgi", "웰시 코기"), T("Short-legged foodie boss", "짧은 다리 먹보 대장"),
        T("A cheerful boss charging about on short legs. Smart and full of energy, but its favourite thing in the world is, without question, food. Its bottom wiggles with every step.",
          "짧은 다리로 씩씩하게 달리는 명랑 대장. 머리가 좋고 활동량이 많지만, 세상에서 제일 좋아하는 건 단연 밥. 걸을 때마다 엉덩이가 실룩실룩해요."),
        0.8f, 0.15f, 0.8f, 0.3f, 0.3f,
        0.9f, 0.85f, 0.7f, 1.4f, 0f,
        0f, 1.2f, 0.5f, 1.1f, TravelMode.Follow,
        new[] { T("Butter", "버터"), T("Toast", "식빵"), T("Waffle", "호떡"), T("Potato", "감자"), T("Nugget", "누룽지") },
        new BreedVoice(
            Greeting: new[] { T("Hi! Got any snacks?", "안녕! 혹시 간식 있어?") },
            Idle: new[] { T("Wiggle wiggle~", "엉덩이 실룩실룩~"), T("Anything to eat?", "뭐 먹을 거 없나?"), T("Short legs, but fast!", "다리는 짧아도 빠르다고!"), T("Let's go!", "출동!") },
            Happy: new[] { T("Yay! Butt dance!", "신난다! 엉덩이 춤!"), T("Woof woof!", "왕왕!") },
            Petted: new[] { T("Pat my bum too!", "궁디 팡팡도 해 줘!"), T("Good, good", "좋다 좋아") },
            Hungry: new[] { T("Food! Food! Food!", "밥! 밥! 밥!"), T("So hungry I could faint…", "배고파서 쓰러질 것 같아…") },
            Sleepy: new[] { T("Full tummy, sleepy…", "배부르니까 졸리다…") },
            Held: new[] { T("I'll stay put like a loaf", "식빵 자세로 안겨 있을게") },
            Rain: new[] { T("Rain? I'll watch it while I eat", "비 오면 밥 먹으면서 보면 되지") },
            Snow: new[] { T("I'll sweep the snow with my belly!", "눈밭은 내 배로 쓸고 다닐게!") },
            Called: new[] { T("Is it dinner?", "밥 주는 거지?"), T("Ran here on my short legs!", "짧은 다리로 달려왔어!") }));

    public static readonly Breed Jindo = new(
        "jindo", PetSpecies.Dog, T("Jindo", "진돗개"), T("Loyal Korean native", "의리의 한국 토박이"),
        T("A loyal soul that guards its chosen person to the end. Clever and clean, wary of strangers. It takes time to open up, but once it does, nobody is more dependable.",
          "한 번 정한 주인은 끝까지 지키는 충성파. 영리하고 깔끔하며 낯선 것엔 경계심이 있어요. 마음을 여는 데 시간이 걸리지만, 한 번 열면 누구보다 든든해요."),
        0.55f, 0.4f, 0.75f, 0.35f, 0.9f,
        0.85f, 1.15f, 1.3f, 1.0f, 0f,
        -0.2f, 1.3f, 0.3f, 0.8f, TravelMode.Follow,
        new[] { T("Baekgu", "백구"), T("Hwanggu", "황구"), T("Goldie", "누렁이"), T("Rocky", "든든이"), T("Boulder", "바위") },
        new BreedVoice(
            Greeting: new[] { T("…Good to meet you. I'll protect you.", "…반갑다. 지켜 줄게.") },
            Idle: new[] { T("All clear around here", "주변 이상 없음"), T("Woof! (on guard)", "멍! (경계 중)"), T("I'm keeping watch", "내가 지키고 있어"), T("Sniff… a strange smell", "킁킁… 낯선 냄새") },
            Happy: new[] { T("I feel safe with you", "주인이랑 있으면 든든해"), T("Woof!", "멍!") },
            Petted: new[] { T("…Nice", "…좋다"), T("Your hand is warm", "주인 손은 따뜻하네") },
            Hungry: new[] { T("I'm hungry. Food?", "배가 고프다. 밥은?") },
            Sleepy: new[] { T("A quick nap, ears open", "잠깐 눈 좀 붙일게, 귀는 열어 둘게") },
            Held: new[] { T("…You don't have to do this", "…이러지 않아도 돼") },
            Rain: new[] { T("A little rain is fine", "이 정도 비는 괜찮아") },
            Snow: new[] { T("Patrol in the snow is fun!", "눈 오는 날 순찰은 즐겁지!") },
            Called: new[] { T("Call me and I'll go anywhere", "부르면 어디든 간다"), T("Here, boss", "왔다, 주인") }));

    // ---------------------------------------------------------------- cats

    public static readonly Breed KoreanShorthair = new(
        "korean-shorthair", PetSpecies.Cat, T("Korean Shorthair", "코리안 숏헤어"), T("Tabby street boss", "골목대장 고등어"),
        T("A mackerel tabby with handsome stripes. Clever, at home anywhere, a born hunter. Independent but warm-hearted: it quietly comes over and sits beside you.",
          "줄무늬가 멋진 고등어 태비. 영리하고 어디서든 잘 적응하는 타고난 사냥꾼이에요. 독립적이지만 정이 많아서 슬쩍 다가와 옆에 앉아요."),
        0.65f, 0.45f, 0.55f, 0.5f, 0.85f,
        1.0f, 1.0f, 1.1f, 1.0f, 0f,
        0f, 0f, 0f, 1.0f, TravelMode.Free,
        new[] { T("Momo", "나비"), T("Tiger", "고등어"), T("Smokey", "까미"), T("Luna", "루루"), T("Mackerel", "보리") },
        new BreedVoice(
            Greeting: new[] { T("Meow. So this is my territory", "냥. 여기가 내 영역이구나") },
            Idle: new[] { T("Mrow?", "냥?"), T("Can I catch that?", "저거 잡아도 돼?"), T("On patrol, meow", "순찰 중이다냥"), T("Don't look at my tail", "꼬리 보지 마") },
            Happy: new[] { T("Purrrr…", "골골골…"), T("Meow meow!", "냥냥!") },
            Petted: new[] { T("Purr… right there", "골골… 거기야"), T("Under the chin too", "턱 밑도 긁어 줘") },
            Hungry: new[] { T("The bowl is empty, meow", "밥 그릇 비었다냥"), T("Meeeooow!", "냐아아옹!") },
            Sleepy: new[] { T("Going to loaf and nap", "식빵 굽고 잘래") },
            Held: new[] { T("…Only for a moment, meow", "…잠깐만이다냥") },
            Rain: new[] { T("Hate rain, meow!", "비 싫다냥!"), T("I don't want to get wet!", "젖는 건 싫어!") },
            Snow: new[] { T("Cold paws…", "발 시려…") },
            Called: new[] { T("Mrow? You called?", "냥? 불렀냥?"), T("Fine, I'll come", "가 줄게") }));

    public static readonly Breed RussianBlue = new(
        "russian-blue", PetSpecies.Cat, T("Russian Blue", "러시안 블루"), T("Shy silver gentleman", "수줍은 은빛 신사"),
        T("A quiet gentleman with a silvery blue coat and green eyes. Very shy at first and likes to hide, but deep and tender with the one person it trusts. Light-footed and graceful.",
          "은빛 회청색 털에 초록 눈을 가진 조용한 신사. 낯을 많이 가려서 처음엔 숨지만, 마음을 연 한 사람에게는 깊고 다정해요. 움직임이 사뿐하고 우아해요."),
        0.45f, 0.8f, 0.45f, 0.75f, 0.95f,
        1.0f, 1.05f, 1.2f, 0.9f, 0.05f,
        0.2f, 0f, 0f, 0.8f, TravelMode.Free,
        new[] { T("Blue", "블루"), T("Silver", "은비"), T("Leo", "레오"), T("Misty", "미스트"), T("Moon", "달이") },
        new BreedVoice(
            Greeting: new[] { T("…Oh, hello. It's a bit new to me…", "…아, 안녕. 조금 낯설어서…") },
            Idle: new[] { T("…", "…"), T("(quietly watching)", "(조용히 지켜보는 중)"), T("Tiptoe, tiptoe", "사뿐사뿐"), T("It's nice and quiet here", "여기 조용해서 좋다") },
            Happy: new[] { T("…I like it", "…좋아"), T("I'm glad it's you", "너라서 좋아") },
            Petted: new[] { T("…Gently, please", "…조심스럽게 해 줘"), T("Purr…", "골골…") },
            Hungry: new[] { T("…I'm hungry", "…배고파") },
            Sleepy: new[] { T("I'll sleep somewhere quiet", "조용한 곳에서 잘래") },
            Held: new[] { T("…I'm trembling", "…떨려"), T("It's okay because it's you", "너니까 괜찮아") },
            Rain: new[] { T("…I like the sound of rain, not the getting wet", "…비 소리는 좋은데, 맞는 건 싫어") },
            Snow: new[] { T("…I'll just watch from the window", "…창밖으로만 볼래") },
            Called: new[] { T("…I'm here", "…왔어"), T("If you call, I'll come", "너가 부르면 갈게") }));

    public static readonly Breed BritishShorthair = new(
        "british-shorthair", PetSpecies.Cat, T("British Shorthair", "브리티시 숏헤어"), T("Laid-back teddy bear", "느긋한 곰돌이"),
        T("A teddy bear of a cat with chubby cheeks and round copper eyes. Calm and easy-going, it spends most of the day lounging. Not keen on being held, but loves sitting right next to you.",
          "통통한 볼에 동그란 주황 눈, 곰인형 같은 고양이. 차분하고 느긋해서 하루 대부분을 뒹굴며 보내요. 안기는 건 별로지만 옆에 붙어 있는 건 좋아해요."),
        0.4f, 0.4f, 0.25f, 0.8f, 0.8f,
        1.1f, 0.85f, 0.7f, 1.1f, 0.1f,
        0.1f, 0f, 0f, 0.95f, TravelMode.Free,
        new[] { T("Teddy", "곰돌이"), T("Mochi", "모찌"), T("Dumpling", "찹쌀이"), T("Tofu", "두부"), T("Chestnut", "밤톨이") },
        new BreedVoice(
            Greeting: new[] { T("Mm… hello. Let's get to know each other slowly", "음… 반가워. 천천히 친해지자") },
            Idle: new[] { T("Nice and slow~", "느긋~하게"), T("Roll, roll", "뒹굴뒹굴"), T("No need to hurry", "서두를 거 없어"), T("(nods off)", "(꾸벅)") },
            Happy: new[] { T("Mm, very satisfying", "음, 만족스러워"), T("Purr", "골골") },
            Petted: new[] { T("Good, carry on", "좋아, 계속") },
            Hungry: new[] { T("It's snack time", "간식 시간이야") },
            Sleepy: new[] { T("I'll think about it after a nap", "한숨 자고 생각할게"), T("Sleeping is my job", "자는 게 일이야") },
            Held: new[] { T("I'd rather sit next to you…", "옆에 앉는 게 더 좋은데…") },
            Rain: new[] { T("Rainy days are for lounging indoors", "비 오는 날엔 집에서 뒹굴기") },
            Snow: new[] { T("I don't like the cold, blanket please", "추운 건 싫어, 담요 줘") },
            Called: new[] { T("Coming, slowly", "천천히 갈게"), T("Mm, here I am", "음, 왔어") }));

    public static readonly Breed ScottishFold = new(
        "scottish-fold", PetSpecies.Cat, T("Scottish Fold", "스코티시 폴드"), T("Folded-ear sweetheart", "접힌 귀 애교쟁이"),
        T("A sweetheart with ears folded forward and a round face. Gentle and people-loving, it quietly follows you around, and its party trick is sitting up like a person: the \"Scottish sit\".",
          "앞으로 폭 접힌 귀와 동그란 얼굴의 애교쟁이. 순하고 사람을 좋아해서 조용히 따라다니고, 사람처럼 퍼질러 앉는 '스코티시 자세'가 특기예요."),
        0.55f, 0.3f, 0.35f, 0.7f, 0.8f,
        1.0f, 0.9f, 0.8f, 1.0f, 0.05f,
        0.1f, 0f, 0f, 1.2f, TravelMode.Follow,
        new[] { T("Muffin", "모모"), T("Button", "꼬미"), T("Pudding", "동글이"), T("Honey", "인절미"), T("Cheddar", "치즈") },
        new BreedVoice(
            Greeting: new[] { T("Hi! Aren't my ears cute?", "안녕! 귀 귀엽지?") },
            Idle: new[] { T("Doing my Scottish sit~", "스코티시 자세 중~"), T("Where are you going? I'll follow", "어디 가? 따라갈래"), T("Round and round", "동글동글"), T("Hehe", "헤헤") },
            Happy: new[] { T("Yay! Mew", "좋아! 냐앙"), T("Purr purr", "골골골") },
            Petted: new[] { T("I love ear rubs", "귀 만지는 거 좋아"), T("Purr…", "골골…") },
            Hungry: new[] { T("Hungry, mew", "배고파, 냐앙") },
            Sleepy: new[] { T("Curling up into a ball to sleep", "동그랗게 말려서 잘래") },
            Held: new[] { T("So cosy", "포근해") },
            Rain: new[] { T("When it rains, indoors is best", "비 오면 집 안이 최고야") },
            Snow: new[] { T("I'll watch the snow through the window", "눈은 창문으로 볼래") },
            Called: new[] { T("Coming, coming!", "갈게갈게!"), T("Mew? Did you call me?", "냐앙? 나 불렀어?") }));

    public static readonly Breed Persian = new(
        "persian", PetSpecies.Cat, T("Persian", "페르시안"), T("Elegant aristocrat", "우아한 귀족 공주"),
        T("An aristocrat with a long, full white coat and a flat face. Quiet and well-mannered, it rarely runs, adores being stroked, and spends most of its time resting elegantly.",
          "길고 풍성한 하얀 털에 납작한 얼굴의 귀족. 조용하고 얌전해서 잘 뛰지 않고, 쓰다듬어 주는 손길을 좋아해요. 대부분의 시간은 우아하게 쉬면서 보내요."),
        0.3f, 0.45f, 0.15f, 0.9f, 0.7f,
        1.1f, 0.75f, 0.5f, 0.95f, 0.15f,
        0.2f, 0f, 0f, 1.0f, TravelMode.Free,
        new[] { T("Princess", "공주"), T("Snow White", "백설이"), T("Cream", "크림"), T("Pearl", "진주"), T("Ruby", "루비") },
        new BreedVoice(
            Greeting: new[] { T("Oh, how do you do. Do take good care of me", "어머, 반가워요. 잘 모셔 주세요") },
            Idle: new[] { T("(sitting elegantly)", "(우아하게 앉아 있는 중)"), T("Running is so undignified", "뛰는 건 품위 없어요"), T("Just tidying my coat", "털 정리 좀 할게요"), T("Yaawn", "후아암") },
            Happy: new[] { T("Most satisfactory", "흡족하군요"), T("Purr…", "골골…") },
            Petted: new[] { T("Yes, brush me just like that", "그래요, 그렇게 빗어 주세요") },
            Hungry: new[] { T("Is dinner not served yet?", "식사 준비는 아직인가요?") },
            Sleepy: new[] { T("It's nap time", "낮잠 시간이에요") },
            Held: new[] { T("Handle me with care, please", "조심히 다뤄 주세요") },
            Rain: new[] { T("My coat must not get wet!", "제 털이 젖으면 안 돼요!") },
            Snow: new[] { T("It's cold, I'll stay inside", "추워요, 실내에 있을래요") },
            Called: new[] { T("I may come slowly, yes?", "천천히 가도 되죠?"), T("You called?", "부르셨나요?") }));

    public static readonly Breed Ragdoll = new(
        "ragdoll", PetSpecies.Cat, T("Ragdoll", "랙돌"), T("Melts when held", "안기면 녹는 인형"),
        T("A big gentle soul with blue eyes and dark points on its face, ears and tail. Pick it up and it goes limp like a rag doll, and it greets you at the door like a dog.",
          "파란 눈에 얼굴·귀·꼬리 끝이 진한 포인트 무늬, 덩치 큰 순둥이. 안아 주면 인형처럼 몸을 축 늘어뜨리고, 강아지처럼 주인을 마중 나와요."),
        0.5f, 0.2f, 0.35f, 0.6f, 0.8f,
        1.05f, 0.9f, 0.8f, 1.1f, 0.05f,
        0.1f, 0f, 0f, 1.3f, TravelMode.Follow,
        new[] { T("Dolly", "인형이"), T("Maple", "마루"), T("Cloud", "구름"), T("Blossom", "봄이"), T("Lala", "라라") },
        new BreedVoice(
            Greeting: new[] { T("Hi~ will you hold me?", "안녕~ 나 안아 줄래?") },
            Idle: new[] { T("Where did you go?", "주인 어디 갔지?"), T("I'll come and meet you", "마중 나갈래"), T("Mew~", "냐앙~"), T("(goes limp)", "(축 늘어짐)") },
            Happy: new[] { T("So nice~ purr purr", "좋아~ 골골골") },
            Petted: new[] { T("Meltiiing…", "녹는다아…"), T("Purr purr purr", "골골골골") },
            Hungry: new[] { T("I want food, mew", "밥 먹고 싶어 냐앙") },
            Sleepy: new[] { T("Let's lie down together", "같이 누워 있자") },
            Held: new[] { T("(goes limp like a doll)", "(인형처럼 축 늘어짐)"), T("Being held is the best…", "안기는 거 제일 좋아…") },
            Rain: new[] { T("On rainy days I'll stay in your arms", "비 오면 안겨 있을래") },
            Snow: new[] { T("Come see the snow with me", "눈 보러 같이 가 줘") },
            Called: new[] { T("Came to meet you!", "마중 왔어!"), T("Mew! I was waiting", "냐앙! 기다렸어") }));

    public static readonly Breed Munchkin = new(
        "munchkin", PetSpecies.Cat, T("Munchkin", "먼치킨"), T("Short-legged troublemaker", "짧은 다리 장난꾸러기"),
        T("A curious little captain that never stops running on its short legs. It loves mischief, and when something catches its eye it stands up on its hind legs like a meerkat to look around.",
          "짧은 다리로 쉴 새 없이 뛰어다니는 호기심 대장. 장난을 좋아하고, 궁금한 게 생기면 뒷다리로 서서 미어캣처럼 두리번거려요."),
        0.85f, 0.25f, 0.7f, 0.4f, 0.75f,
        0.95f, 0.85f, 0.6f, 1.0f, 0f,
        0.1f, 0f, 0f, 1.1f, TravelMode.Free,
        new[] { T("Pip", "짤똥이"), T("Cheese", "치즈"), T("Walnut", "호두"), T("Peanut", "땅콩"), T("Tiny", "꼬마") },
        new BreedVoice(
            Greeting: new[] { T("Hi! Don't underestimate my short legs!", "안녕! 나 다리 짧다고 무시하지 마!") },
            Idle: new[] { T("(looking around like a meerkat)", "(미어캣 자세로 두리번)"), T("What's that? What's that?", "저거 뭐야? 뭐야?"), T("Zoom zoom zoom!", "다다다다!"), T("Anything to get up to?", "장난칠 거 없나?") },
            Happy: new[] { T("So fun, meow!", "신난다냥!"), T("Again, again!", "또또!") },
            Petted: new[] { T("Hehe, I like it!", "헤헤, 좋아!") },
            Hungry: new[] { T("So hungry my legs are getting shorter!", "배고파서 더 짧아지겠어!") },
            Sleepy: new[] { T("Played too much… sleepy", "너무 놀았나… 졸려") },
            Held: new[] { T("My legs are dangling!", "다리가 대롱대롱!") },
            Rain: new[] { T("Rain! Take cover!", "비다! 피해!") },
            Snow: new[] { T("My legs are buried in snow…", "다리가 눈에 파묻혀…") },
            Called: new[] { T("Full sprint on short legs!", "짧은 다리로 전력 질주!"), T("Here, meow!", "왔다냥!") }));

    public static readonly IReadOnlyList<Breed> All = new[]
    {
        Pomeranian, Maltese, Poodle, Bichon, Shiba, Corgi, Jindo,
        KoreanShorthair, RussianBlue, BritishShorthair, ScottishFold, Persian, Ragdoll, Munchkin,
    };

    public static IEnumerable<Breed> Of(PetSpecies species) => All.Where(b => b.Species == species);

    /// <summary>Pets from before breeds existed: the grey tabby is a Korean shorthair, the orange dog a Pomeranian.</summary>
    public static Breed Default(PetSpecies species) => species == PetSpecies.Cat ? KoreanShorthair : Pomeranian;

    public static Breed Find(string? id, PetSpecies species) =>
        All.FirstOrDefault(b => b.Id == id && b.Species == species) ?? Default(species);
}
