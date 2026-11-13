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

    // ---------------------------------------------------------------- more dogs

    public static readonly Breed GoldenRetriever = new(
        "golden-retriever", PetSpecies.Dog, T("Golden Retriever", "골든 리트리버"), T("Sunny best friend", "햇살 같은 단짝"),
        T("A big golden heart that loves everyone it meets. Gentle and patient, it fetches anything you throw, never says no to a swim, and is happiest bringing you a present.",
          "만나는 모두를 좋아하는 커다란 황금빛 마음. 순하고 참을성이 많아서 던지는 건 뭐든 물어 오고, 물놀이라면 마다하지 않고, 선물을 물어다 줄 때 제일 행복해요."),
        0.85f, 0.05f, 0.75f, 0.35f, 0.4f,
        0.9f, 1.1f, 1.15f, 1.3f, 0f,
        -0.2f, 1.1f, 1.0f, 1.4f, TravelMode.Follow,
        new[] { T("Sunny", "해피"), T("Honey", "꿀이"), T("Goldie", "금동이"), T("Buddy", "복실이"), T("Maple", "단풍이") },
        new BreedVoice(
            Greeting: new[] { T("Hi! I love you already!", "안녕! 벌써 좋아졌어!") },
            Idle: new[] { T("Want to play? Want to play?", "놀래? 놀래?"), T("I brought you something!", "선물 가져왔어!"), T("Everyone is my friend", "모두 내 친구야"), T("Is there water anywhere?", "어디 물 없나?") },
            Happy: new[] { T("Best. Day. Ever!", "오늘 최고야!"), T("Tail can't stop wagging!", "꼬리가 멈추질 않아!") },
            Petted: new[] { T("More, more, more!", "더, 더, 더!"), T("I love you too!", "나도 사랑해!") },
            Hungry: new[] { T("Is that food? Can I have some?", "그거 밥이야? 나도 줘?") },
            Sleepy: new[] { T("Big yawn… nap by your feet", "하아암… 발밑에서 잘게") },
            Held: new[] { T("I'm too big for this, but okay!", "나 좀 큰데, 그래도 좋아!") },
            Rain: new[] { T("Rain! Puddles! Yay!", "비다! 물웅덩이다! 신난다!") },
            Snow: new[] { T("Snow angels!", "눈밭에서 뒹굴기!") },
            Called: new[] { T("Coming! Coming!", "간다! 간다!"), T("You called me!", "나 불렀지!") }));

    public static readonly Breed FrenchBulldog = new(
        "french-bulldog", PetSpecies.Dog, T("French Bulldog", "프렌치 불도그"), T("Snoring comedian", "코 고는 개그맨"),
        T("A little clown with bat ears and a squashed nose. Easy-going and affectionate, it would rather lounge than run, snores loudly in its sleep, and can't stand the heat or the rain.",
          "박쥐 귀에 납작코를 한 작은 개그맨. 느긋하고 다정해서 뛰는 것보다 뒹구는 걸 좋아하고, 잘 때는 코를 크게 골며, 더위와 비는 질색이에요."),
        0.65f, 0.2f, 0.35f, 0.75f, 0.4f,
        1.15f, 0.85f, 0.6f, 1.1f, 0.1f,
        0.3f, 0.7f, 0.1f, 1.2f, TravelMode.Follow,
        new[] { T("Bruno", "두부"), T("Coco", "감자"), T("Biscuit", "만두"), T("Pug", "떡볶이"), T("Louie", "호빵") },
        new BreedVoice(
            Greeting: new[] { T("Snort! Hello there!", "킁! 안녕!") },
            Idle: new[] { T("Snort snort", "킁킁"), T("Let's just lie here", "그냥 여기 누워 있자"), T("Did someone say snack?", "누가 간식이라고 했어?"), T("(funny face)", "(웃긴 표정)") },
            Happy: new[] { T("Snort! Happy!", "킁! 좋아!"), T("Frenchie wiggle!", "엉덩이 흔들기!") },
            Petted: new[] { T("Mmm, snort", "음, 킁"), T("Scratch the ears!", "귀 긁어 줘!") },
            Hungry: new[] { T("My tummy is talking", "배에서 소리 나") },
            Sleepy: new[] { T("Zzz… snoooore", "드르렁… 쿨") },
            Held: new[] { T("Heavy, huh? Hehe", "무겁지? 헤헤") },
            Rain: new[] { T("Nope. Not walking in that.", "싫어. 저기 안 걸을래.") },
            Snow: new[] { T("Cold paws, cold nose…", "발도 코도 시려…") },
            Called: new[] { T("Waddling over!", "뒤뚱뒤뚱 간다!"), T("Snort? Me?", "킁? 나?") }));

    public static readonly Breed Dachshund = new(
        "dachshund", PetSpecies.Dog, T("Dachshund", "닥스훈트"), T("Brave little sausage", "용감한 소시지"),
        T("A long body on tiny legs and a heart much bigger than both. Brave, curious and a little stubborn, it loves to burrow under blankets and to dig for treasure.",
          "작은 다리 위의 긴 몸, 그리고 그보다 훨씬 큰 용기. 용감하고 호기심 많고 조금 고집스러워서, 담요 속에 파고들기와 보물찾기 땅파기를 좋아해요."),
        0.7f, 0.25f, 0.65f, 0.55f, 0.45f,
        1.0f, 0.85f, 0.5f, 1.1f, 0.05f,
        0.25f, 0.8f, 0.4f, 1.0f, TravelMode.Follow,
        new[] { T("Frank", "소시지"), T("Oscar", "닥스"), T("Pretzel", "꽈배기"), T("Peanut", "땅콩"), T("Noodle", "국수") },
        new BreedVoice(
            Greeting: new[] { T("Hello! I'm long and proud of it!", "안녕! 길쭉한 게 내 매력이야!") },
            Idle: new[] { T("Dig, dig, dig!", "파자, 파자!"), T("Something's under there…", "저 밑에 뭔가 있어…"), T("I'm not small, I'm compact", "작은 게 아니라 알찬 거야"), T("Blanket fort time", "담요 속으로 쏙") },
            Happy: new[] { T("Wiggle all the way down!", "꼬리부터 엉덩이까지 신나!"), T("Woof!", "왈!") },
            Petted: new[] { T("Belly too, please", "배도 긁어 줘") },
            Hungry: new[] { T("Long body, long hunger", "몸이 길어서 배도 금방 고파") },
            Sleepy: new[] { T("Burrowing in for a nap", "파고들어 낮잠 잘래") },
            Held: new[] { T("Support my back end!", "엉덩이 받쳐 줘!") },
            Rain: new[] { T("My belly gets wet first!", "배가 제일 먼저 젖어!") },
            Snow: new[] { T("Snow tunnel!", "눈 터널이다!") },
            Called: new[] { T("Running as fast as these legs go!", "이 다리로 최대한 빨리!"), T("Here!", "왔어!") }));

    public static readonly Breed SiberianHusky = new(
        "siberian-husky", PetSpecies.Dog, T("Siberian Husky", "시베리안 허스키"), T("Dramatic snow runner", "수다쟁이 눈썰매꾼"),
        T("A wolf-faced athlete with ice-blue eyes and endless stamina. Friendly to everyone, famous for loud \"talking\" back, an escape artist at heart, and never happier than in the snow.",
          "늑대 같은 얼굴에 얼음처럼 파란 눈, 끝없는 체력의 운동선수. 누구에게나 친근하고, 크게 말대꾸하는 걸로 유명하고, 타고난 탈출 대장이며, 눈밭에서 제일 행복해요."),
        0.8f, 0.15f, 0.9f, 0.2f, 0.7f,
        0.8f, 1.25f, 1.3f, 1.0f, 0f,
        -0.1f, 1.6f, 0.4f, 0.9f, TravelMode.Free,
        new[] { T("Blizzard", "눈보라"), T("Luna", "루나"), T("Koda", "코다"), T("Storm", "폭풍이"), T("Nova", "별빛") },
        new BreedVoice(
            Greeting: new[] { T("Awoo-woo! Hello!", "아우우! 안녕!") },
            Idle: new[] { T("Awoooo!", "아우우우!"), T("Woo-woo-woo (talking back)", "우우우 (말대꾸 중)"), T("I could run all day", "하루 종일 달릴 수 있어"), T("Is that gate open…?", "저 문 열려 있나…?") },
            Happy: new[] { T("AWOO! Let's run!", "아우! 달리자!") },
            Petted: new[] { T("Woo-woo, nice", "우우, 좋아") },
            Hungry: new[] { T("Awoo? Dinner?", "아우? 밥?") },
            Sleepy: new[] { T("Curling my tail over my nose", "꼬리로 코 덮고 잘래") },
            Held: new[] { T("Woo! Put me down, I want to run!", "우! 내려 줘, 달리고 싶어!") },
            Rain: new[] { T("Rain is fine. Snow is better.", "비도 괜찮아. 눈이면 더 좋고.") },
            Snow: new[] { T("SNOW! This is my weather!", "눈이다! 내 날씨야!"), T("Awoooo in the snow!", "눈 속에서 아우우우!") },
            Called: new[] { T("Awoo, on my way!", "아우, 간다!"), T("Fine, I'll come. Woo.", "알았어, 갈게. 우.") }));

    public static readonly Breed Chihuahua = new(
        "chihuahua", PetSpecies.Dog, T("Chihuahua", "치와와"), T("Tiny bodyguard", "작은 경호원"),
        T("The smallest dog with the biggest attitude. Fiercely loyal to its one person, suspicious of everyone else, always shivering a little, and happiest tucked up somewhere warm.",
          "가장 작은 몸에 가장 큰 배짱. 자기 사람 하나에게만 충성하고 다른 이는 일단 의심하며, 늘 조금씩 떨고, 따뜻한 곳에 쏙 들어가 있을 때 제일 행복해요."),
        0.55f, 0.6f, 0.6f, 0.75f, 0.6f,
        1.05f, 0.9f, 0.9f, 0.85f, 0.05f,
        0.6f, 0.4f, 0.1f, 0.9f, TravelMode.Follow,
        new[] { T("Taco", "콩알이"), T("Chico", "쪼꼬"), T("Bean", "꼬맹이"), T("Pepper", "후추"), T("Pixie", "요정이") },
        new BreedVoice(
            Greeting: new[] { T("Yip! I'm watching you. …Hi.", "앙! 지켜보고 있다. …안녕.") },
            Idle: new[] { T("Yip yip! Who's there?!", "앙앙! 누구야?!"), T("(shivering)", "(오들오들)"), T("I'm guarding you", "내가 지켜 줄게"), T("Too cold. Need a blanket.", "추워. 담요 필요해.") },
            Happy: new[] { T("Yip! You're my favourite!", "앙! 네가 제일 좋아!") },
            Petted: new[] { T("Only you can do that", "너만 이거 해도 돼") },
            Hungry: new[] { T("Tiny tummy, big hunger!", "배는 작아도 배고픔은 커!") },
            Sleepy: new[] { T("Tucking in somewhere warm", "따뜻한 데 쏙 들어가 잘래") },
            Held: new[] { T("Up here is where I belong", "여기가 내 자리지") },
            Rain: new[] { T("Rain?! Absolutely not!", "비?! 절대 안 돼!") },
            Snow: new[] { T("Too cold! Too cold!", "너무 추워! 너무 추워!") },
            Called: new[] { T("Yip! Coming, my human!", "앙! 간다, 내 사람!"), T("Here, here!", "여기, 여기!") }));

    public static readonly Breed Beagle = new(
        "beagle", PetSpecies.Dog, T("Beagle", "비글"), T("Nose-first adventurer", "코로 사는 탐험가"),
        T("A merry hound that follows its nose wherever it leads. Cheerful, friendly and endlessly curious, it howls when it finds something exciting and is always, always hungry.",
          "코가 이끄는 대로 어디든 가는 명랑한 사냥개. 쾌활하고 친근하고 끝없이 궁금해하며, 신나는 걸 찾으면 길게 울고, 언제나 언제나 배가 고파요."),
        0.8f, 0.15f, 0.8f, 0.3f, 0.3f,
        0.9f, 1.05f, 1.1f, 1.4f, 0f,
        0.1f, 1.1f, 0.4f, 1.1f, TravelMode.Free,
        new[] { T("Snoopy", "뽀삐"), T("Copper", "구리"), T("Rusty", "망고"), T("Daisy", "꼬순이"), T("Scout", "탐정이") },
        new BreedVoice(
            Greeting: new[] { T("Sniff sniff… you smell nice! Hi!", "킁킁… 좋은 냄새! 안녕!") },
            Idle: new[] { T("Sniff sniff sniff", "킁킁킁"), T("Aroooo! Found something!", "아우우! 뭔가 찾았다!"), T("This trail goes somewhere…", "이 냄새 어디로 가지…"), T("Is that food I smell?", "밥 냄새 아니야?") },
            Happy: new[] { T("Arooo! Happy!", "아우우! 신나!") },
            Petted: new[] { T("Ears too, please!", "귀도 만져 줘!") },
            Hungry: new[] { T("I'm always hungry. Always.", "난 항상 배고파. 항상.") },
            Sleepy: new[] { T("Even noses need a nap", "코도 쉬어야지") },
            Held: new[] { T("Sniffing from up here!", "여기서도 킁킁!") },
            Rain: new[] { T("Rain makes everything smell new!", "비 오면 냄새가 다 새로워!") },
            Snow: new[] { T("Snow smells like… snow!", "눈 냄새는… 눈 냄새!") },
            Called: new[] { T("Following your scent!", "네 냄새 따라왔어!"), T("Aroo, here!", "아우, 왔어!") }));

    // ---------------------------------------------------------------- more cats

    public static readonly Breed Siamese = new(
        "siamese", PetSpecies.Cat, T("Siamese", "샴"), T("Chatty shadow", "수다쟁이 그림자"),
        T("A sleek cream cat with dark points and sapphire eyes, and the loudest opinions in the house. Clever, demanding and devoted, it follows you room to room and talks the whole way.",
          "진한 포인트 무늬와 사파이어 눈의 날씬한 크림색 고양이, 그리고 집에서 제일 큰 목소리. 영리하고 요구가 많고 헌신적이라 방마다 따라다니며 내내 말을 걸어요."),
        0.7f, 0.2f, 0.7f, 0.45f, 0.85f,
        1.0f, 1.1f, 1.25f, 1.0f, 0f,
        0.2f, 0f, 0f, 1.2f, TravelMode.Follow,
        new[] { T("Mango", "망고"), T("Sapphire", "사파이어"), T("Mocha", "모카"), T("Kiki", "키키"), T("Siam", "샤미") },
        new BreedVoice(
            Greeting: new[] { T("MRAOW! Finally, you're here!", "냐아옹! 드디어 왔구나!") },
            Idle: new[] { T("Mraow? MRAOW!", "냐옹? 냐아옹!"), T("Where are you going? I'm coming", "어디 가? 나도 가"), T("Let me tell you about my day", "내 하루 얘기 들어 볼래"), T("Pay attention to me", "나한테 집중해") },
            Happy: new[] { T("Purr-mraow!", "골골냐옹!") },
            Petted: new[] { T("Yes. Exactly there. Mraow.", "그래. 바로 거기. 냐옹.") },
            Hungry: new[] { T("MRAOW! FOOD!", "냐아옹! 밥!") },
            Sleepy: new[] { T("Nap on your lap. Now.", "무릎에서 잘래. 지금.") },
            Held: new[] { T("Acceptable. For now.", "괜찮아. 일단은.") },
            Rain: new[] { T("Mraow! I object to rain!", "냐옹! 비에 반대야!") },
            Snow: new[] { T("Cold! Complaining loudly!", "추워! 크게 항의할 거야!") },
            Called: new[] { T("Coming, and talking about it!", "가면서 말할게!"), T("Mraow? You missed me", "냐옹? 보고 싶었지") }));

    public static readonly Breed MaineCoon = new(
        "maine-coon", PetSpecies.Cat, T("Maine Coon", "메인쿤"), T("Gentle giant", "다정한 거인"),
        T("A big shaggy tabby with lynx-tipped ears and a plume of a tail. Gentle, sociable and a little dog-like, it chirps instead of meowing and is fascinated by running water.",
          "스라소니 같은 귀털과 깃털 같은 꼬리를 가진 커다란 털복숭이 태비. 순하고 사교적이고 조금 강아지 같아서, 야옹 대신 짹짹 소리를 내고 흐르는 물을 신기해해요."),
        0.65f, 0.2f, 0.5f, 0.55f, 0.75f,
        1.0f, 0.95f, 0.9f, 1.2f, 0.05f,
        0.05f, 0f, 0f, 1.2f, TravelMode.Follow,
        new[] { T("Bear", "곰탱이"), T("Thor", "토르"), T("Willow", "버들이"), T("Maple", "단풍"), T("Rufus", "왕자") },
        new BreedVoice(
            Greeting: new[] { T("Chirrup! Hello, friend", "짹! 안녕, 친구") },
            Idle: new[] { T("Chirp chirp", "짹짹"), T("Is that water running?", "물 흐르는 소리야?"), T("Big cat, big heart", "몸도 크고 마음도 커"), T("Trill?", "구르릉?") },
            Happy: new[] { T("Chirrup! Purrrr", "짹! 골골골") },
            Petted: new[] { T("Brush my ruff, please", "목털 빗어 줘") },
            Hungry: new[] { T("Chirp? Big cats need big meals", "짹? 큰 고양이는 많이 먹어야 해") },
            Sleepy: new[] { T("Stretching out across everything", "길게 늘어져서 잘래") },
            Held: new[] { T("I'm heavy, but I'm soft", "무겁지만 푹신해") },
            Rain: new[] { T("A little rain doesn't bother this coat", "이 털이면 비 조금은 괜찮아") },
            Snow: new[] { T("My fur was made for this", "내 털은 이럴 때 쓰는 거야") },
            Called: new[] { T("Chirrup, coming!", "짹, 간다!"), T("Here I am", "나 왔어") }));

    public static readonly Breed Bengal = new(
        "bengal", PetSpecies.Cat, T("Bengal", "벵갈"), T("Mini leopard", "작은 표범"),
        T("A golden, wild-patterned athlete that looks like a little leopard. Bold and endlessly energetic, it climbs everything, plays fetch, and is oddly fond of water.",
          "작은 표범처럼 보이는 황금빛 야생 무늬의 운동선수. 대담하고 에너지가 끝이 없어서 뭐든 기어오르고, 공을 물어 오고, 이상하게 물을 좋아해요."),
        0.9f, 0.2f, 0.9f, 0.25f, 0.8f,
        0.9f, 1.2f, 1.45f, 1.05f, 0f,
        0f, 0f, 0f, 0.9f, TravelMode.Free,
        new[] { T("Leo", "레오"), T("Jungle", "정글"), T("Amber", "호박"), T("Zara", "자라"), T("Tiger", "호랭이") },
        new BreedVoice(
            Greeting: new[] { T("Rrrow! Let's go hunting!", "르르옹! 사냥 가자!") },
            Idle: new[] { T("Climb! Jump! Pounce!", "오르고! 뛰고! 덮쳐!"), T("Got anything to chase?", "쫓을 거 없어?"), T("I'm a leopard, really", "사실 나 표범이야"), T("Bored. Entertain me.", "심심해. 놀아 줘.") },
            Happy: new[] { T("Rrrow! Again!", "르르옹! 또!") },
            Petted: new[] { T("Okay, quick one, then play", "좋아, 짧게. 그리고 놀자") },
            Hungry: new[] { T("Hunter needs dinner", "사냥꾼도 밥은 먹어야지") },
            Sleepy: new[] { T("Recharging for more zoomies", "다시 뛰려고 충전 중") },
            Held: new[] { T("Let me go, I have climbing to do!", "놔 줘, 올라갈 데가 있어!") },
            Rain: new[] { T("Water? I don't mind it, actually", "물? 사실 괜찮아") },
            Snow: new[] { T("Pouncing on snowflakes!", "눈송이 덮치기!") },
            Called: new[] { T("Leaping over!", "점프해서 간다!"), T("Rrrow, here!", "르르옹, 왔어!") }));

    public static readonly Breed Sphynx = new(
        "sphynx", PetSpecies.Cat, T("Sphynx", "스핑크스"), T("Warm velvet cuddler", "따끈한 벨벳 껌딱지"),
        T("A hairless cat with huge ears and skin like warm velvet. Extremely affectionate and a little mischievous, it seeks out laps and sunny spots, and really dislikes the cold.",
          "커다란 귀에 따뜻한 벨벳 같은 피부를 가진 털 없는 고양이. 무척 다정하고 조금 장난스러워서 무릎과 햇볕을 찾아다니고, 추운 걸 정말 싫어해요."),
        0.75f, 0.1f, 0.6f, 0.65f, 0.7f,
        1.05f, 1.0f, 1.15f, 1.2f, 0.05f,
        0.5f, 0f, 0f, 1.3f, TravelMode.Follow,
        new[] { T("Ramses", "람세스"), T("Pharaoh", "파라오"), T("Velvet", "벨벳"), T("Gizmo", "기즈모"), T("Peach", "복숭아") },
        new BreedVoice(
            Greeting: new[] { T("Hello! Is your lap warm?", "안녕! 무릎 따뜻해?") },
            Idle: new[] { T("Where's the sunny spot?", "햇볕 어디 있어?"), T("Cuddle me, I'm chilly", "안아 줘, 좀 추워"), T("Velvet, not bald", "대머리 아니고 벨벳이야"), T("Mrr?", "므르?") },
            Happy: new[] { T("Warm and happy, purr", "따뜻하고 좋아, 골골") },
            Petted: new[] { T("Ooh, warm hands!", "오, 손 따뜻해!") },
            Hungry: new[] { T("Staying warm takes a lot of food", "따뜻하려면 많이 먹어야 해") },
            Sleepy: new[] { T("Under the blanket, please", "담요 속에서 잘래") },
            Held: new[] { T("Yes! Body heat!", "좋아! 체온!") },
            Rain: new[] { T("Cold and wet? No thank you!", "춥고 젖는 거? 사양할게!") },
            Snow: new[] { T("Brrr! Sweater, please!", "부르르! 스웨터 줘!") },
            Called: new[] { T("Coming for cuddles!", "안기러 간다!"), T("Mrr, here", "므르, 왔어") }));

    public static readonly Breed Abyssinian = new(
        "abyssinian", PetSpecies.Cat, T("Abyssinian", "아비시니안"), T("Curious explorer", "호기심 많은 탐험가"),
        T("A slender, ruddy cat with big ears and a glowing ticked coat, built for adventure. Busy, clever and playful, it has to see what you are doing and climb to the highest spot.",
          "커다란 귀와 반짝이는 붉은 털의 날씬한 고양이, 모험을 위해 태어났어요. 바쁘고 영리하고 장난스러워서, 뭘 하는지 꼭 봐야 하고 제일 높은 곳에 올라가야 해요."),
        0.85f, 0.25f, 0.85f, 0.3f, 0.85f,
        0.95f, 1.15f, 1.4f, 1.0f, 0f,
        0.15f, 0f, 0f, 1.0f, TravelMode.Free,
        new[] { T("Cleo", "클레오"), T("Rusty", "루비"), T("Sahara", "사하라"), T("Copper", "구리"), T("Nile", "나일") },
        new BreedVoice(
            Greeting: new[] { T("Oh, hello! What's that? And that?", "어, 안녕! 저건 뭐야? 저건?") },
            Idle: new[] { T("What are you doing? Show me!", "뭐 해? 보여 줘!"), T("Higher! Higher!", "더 높이! 더 높이!"), T("Exploring…", "탐험 중…"), T("Busy busy busy", "바빠 바빠") },
            Happy: new[] { T("Mrrp! Fun!", "므릅! 재밌다!") },
            Petted: new[] { T("Quick pet, then adventure", "짧게 쓰다듬고 모험 가자") },
            Hungry: new[] { T("Explorers need snacks", "탐험가도 간식은 필요해") },
            Sleepy: new[] { T("Just a short rest up high", "높은 데서 잠깐 쉴게") },
            Held: new[] { T("I can see more from up here!", "여기서 더 잘 보여!") },
            Rain: new[] { T("I'll explore indoors today", "오늘은 실내 탐험이야") },
            Snow: new[] { T("Interesting… but cold", "신기한데… 추워") },
            Called: new[] { T("On my way, investigating!", "조사하면서 갈게!"), T("Mrrp, here!", "므릅, 왔어!") }));

    // ---------------------------------------------------------------- more dogs

    public static readonly Breed Samoyed = new(
        "samoyed", PetSpecies.Dog, T("Samoyed", "사모예드"), T("Smiling snow cloud", "웃는 눈구름"),
        T("A big white cloud with a smile that never switches off. Friendly to everyone, chatty in a soft woo-woo way, and never happier than rolling in fresh snow.",
          "웃는 얼굴이 꺼지지 않는 커다란 하얀 구름. 누구에게나 다정하고, 우우 하며 부드럽게 수다를 떨고, 갓 내린 눈밭에서 뒹굴 때 제일 행복해요."),
        0.8f, 0.1f, 0.7f, 0.3f, 0.45f,
        1.0f, 1.05f, 1.0f, 1.1f, 0f,
        0.1f, 1.6f, 0.5f, 1.3f, TravelMode.Follow,
        new[] { T("Cloud", "구름이"), T("Snowy", "눈송이"), T("Mallow", "마시멜로"), T("Polar", "북극이"), T("Sugar", "설탕이") },
        new BreedVoice(
            Greeting: new[] { T("Woo-woo! Hi, friend!", "우우! 안녕, 친구!") },
            Idle: new[] { T("Smiling is my job", "웃는 게 내 일이야"), T("Woo-woo-woo", "우우우~"), T("Is it snowing yet?", "아직 눈 안 와?"), T("Everyone's my friend", "다 내 친구야") },
            Happy: new[] { T("Big Sammy smile!", "사모 스마일!"), T("Woo-hoo!", "우후!") },
            Petted: new[] { T("Fluff me more!", "털 더 쓰다듬어 줘!"), T("Mmm, so soft, right?", "음~ 부드럽지?") },
            Hungry: new[] { T("Woo… my tummy's talking", "우… 배가 말해") },
            Sleepy: new[] { T("Curling into a cloud", "구름처럼 동그랗게 잘게") },
            Held: new[] { T("I'm heavier than I look!", "보기보다 무거워!") },
            Rain: new[] { T("My white coat! Not the mud!", "하얀 털 안 돼! 진흙은 싫어!") },
            Snow: new[] { T("SNOW! This is my home!", "눈이다! 여기가 내 고향이야!"), T("Rolling, rolling!", "데굴데굴!") },
            Called: new[] { T("Woo-woo, coming!", "우우, 간다!") }));

    public static readonly Breed BorderCollie = new(
        "border-collie", PetSpecies.Dog, T("Border Collie", "보더 콜리"), T("Genius herder", "천재 양치기"),
        T("The cleverest dog around and it knows it. Always busy, always watching, it herds the other pets into neat little groups and learns any trick in one go.",
          "제일 똑똑한 강아지이고 본인도 알아요. 늘 바쁘고 늘 지켜보면서, 다른 펫들을 가지런히 몰아 모으고, 어떤 재주든 한 번에 배워요."),
        0.9f, 0.2f, 0.95f, 0.15f, 0.6f,
        0.85f, 1.3f, 1.3f, 1.0f, -0.1f,
        0f, 0.9f, 0.6f, 1.2f, TravelMode.Free,
        new[] { T("Scout", "척척이"), T("Pepper", "후추"), T("Domino", "도미노"), T("Echo", "메아리"), T("Jet", "번개") },
        new BreedVoice(
            Greeting: new[] { T("Hello. Shall we get to work?", "안녕. 일 시작할까?") },
            Idle: new[] { T("Everyone, line up!", "모두 줄 서!"), T("I'm watching. Always.", "다 보고 있어. 항상."), T("Give me a job!", "일 좀 줘!"), T("Got a new trick for me?", "새 재주 없어?") },
            Happy: new[] { T("Nailed it!", "완벽해!"), T("Again, faster!", "또, 더 빨리!") },
            Petted: new[] { T("Good, but what's next?", "좋아, 근데 다음은?") },
            Hungry: new[] { T("Fuel for the brain, please", "두뇌 연료 주세요") },
            Sleepy: new[] { T("Brain… off… for a bit", "두뇌… 잠깐… 끌게") },
            Held: new[] { T("I can't herd from up here!", "여기선 못 몰잖아!") },
            Rain: new[] { T("Work goes on, rain or shine", "비가 와도 일은 해") },
            Snow: new[] { T("Snow zoomies!", "눈밭 질주!") },
            Called: new[] { T("Already on my way", "벌써 가는 중이야"), T("Reporting!", "도착 보고!") }));

    public static readonly Breed ShihTzu = new(
        "shih-tzu", PetSpecies.Dog, T("Shih Tzu", "시츄"), T("Little lion royal", "꼬마 사자 왕족"),
        T("A palace dog with a lion's mane and a lapdog's heart. Calm, sweet and a little proud, it would much rather be carried than walk, and it naps like royalty.",
          "사자 갈기를 한 궁궐 강아지, 마음은 무릎 강아지예요. 차분하고 다정하고 조금 도도해서, 걷는 것보다 안기는 걸 좋아하고 왕족처럼 낮잠을 자요."),
        0.45f, 0.25f, 0.3f, 0.85f, 0.75f,
        1.15f, 0.8f, 0.6f, 1.0f, 0.2f,
        0.6f, 0.3f, 0f, 1.15f, TravelMode.Follow,
        new[] { T("Bao", "바오"), T("Lulu", "루루"), T("Ming", "밍밍"), T("Toffee", "토피"), T("Duchess", "공작부인") },
        new BreedVoice(
            Greeting: new[] { T("You may pet me now", "이제 쓰다듬어도 좋아") },
            Idle: new[] { T("Carry me, please", "안아서 옮겨 줘"), T("My mane is perfect today", "오늘 갈기 완벽해"), T("A cushion would be nice", "방석 있으면 좋겠다"), T("Hmph, too much walking", "흥, 너무 많이 걸었어") },
            Happy: new[] { T("How delightful!", "아주 흡족해!") },
            Petted: new[] { T("Yes, the ears too", "그래, 귀도"), T("Royal treatment, finally", "드디어 왕족 대접") },
            Hungry: new[] { T("Dinner should be served", "식사를 올려라") },
            Sleepy: new[] { T("Royal nap time", "왕실 낮잠 시간") },
            Held: new[] { T("This is the right way to travel", "이게 바른 이동 방법이지") },
            Rain: new[] { T("Not in this weather, thank you", "이런 날씨엔 사양할게") },
            Snow: new[] { T("Cold paws… carry me", "발 시려… 안아 줘") },
            Called: new[] { T("I'm coming… slowly", "가고 있어… 천천히") }));

    public static readonly Breed Pug = new(
        "pug", PetSpecies.Dog, T("Pug", "퍼그"), T("Wrinkly little clown", "주름 많은 꼬마 광대"),
        T("A wrinkly face, a curly tail and a snort for every occasion. A charming clown who loves food, naps and you, in that order on most days.",
          "주름진 얼굴, 돌돌 말린 꼬리, 상황마다 다른 킁킁 소리. 먹을 것, 낮잠, 그리고 주인을 좋아하는 매력 만점 광대예요. 보통은 그 순서대로요."),
        0.75f, 0.1f, 0.3f, 0.75f, 0.35f,
        1.25f, 0.75f, 0.5f, 1.5f, 0.3f,
        0.4f, 0f, -0.3f, 1.3f, TravelMode.Follow,
        new[] { T("Dumpling", "만두"), T("Mochi", "모찌"), T("Otis", "오티스"), T("Peanut", "땅콩"), T("Waffle", "와플") },
        new BreedVoice(
            Greeting: new[] { T("Snort! Hi! Snort!", "킁! 안녕! 킁!") },
            Idle: new[] { T("Snort snort", "킁킁"), T("Did someone say snack?", "누가 간식이라고 했어?"), T("My wrinkles have wrinkles", "내 주름에도 주름이 있어"), T("Nap? Nap.", "낮잠? 낮잠.") },
            Happy: new[] { T("Snort-snort-snort!", "킁킁킁!"), T("Pug zoomies!", "퍼그 질주!") },
            Petted: new[] { T("Rub the wrinkles!", "주름 쓰다듬어 줘!"), T("Hrrmph, happy", "흐릉, 행복해") },
            Hungry: new[] { T("Hungry. Always hungry.", "배고파. 언제나 배고파.") },
            Sleepy: new[] { T("Snooore…", "드르렁…") },
            Held: new[] { T("I'm a loaf of bread now", "나 이제 식빵이야") },
            Rain: new[] { T("Nope. Too wet.", "싫어. 너무 축축해.") },
            Snow: new[] { T("Too cold for my face!", "얼굴이 너무 시려!") },
            Called: new[] { T("Snort! On my way!", "킁! 간다!") }));

    // ---------------------------------------------------------------- more cats

    public static readonly Breed AmericanShorthair = new(
        "american-shorthair", PetSpecies.Cat, T("American Shorthair", "아메리칸 숏헤어"), T("Silver tabby all-rounder", "은빛 줄무늬 만능캣"),
        T("Silver swirls and an easy-going heart. Healthy, playful and sensible, it gets along with everyone, keeps the house free of 'intruders' and never makes a fuss.",
          "은빛 소용돌이 무늬에 느긋한 마음. 튼튼하고 장난기 많고 똑똑해서 누구와도 잘 지내고, 집 안 '침입자'도 잡고, 투정을 부리지 않아요."),
        0.7f, 0.2f, 0.6f, 0.5f, 0.6f,
        1.0f, 1.0f, 1.1f, 1.1f, 0.05f,
        0.5f, 0.2f, 0f, 1.15f, TravelMode.Free,
        new[] { T("Silver", "은동이"), T("Smokey", "연기"), T("Jasper", "재스퍼"), T("Misty", "안개"), T("Ash", "잿빛이") },
        new BreedVoice(
            Greeting: new[] { T("Mrrow. Nice to see you.", "므로우. 반가워.") },
            Idle: new[] { T("Patrolling the house", "집 순찰 중"), T("Anything to hunt?", "사냥할 거 없나?"), T("All good here", "여긴 이상 없음"), T("Mrrp?", "므릅?") },
            Happy: new[] { T("Mrrrow!", "므르르!") },
            Petted: new[] { T("That's nice, carry on", "좋아, 계속해") },
            Hungry: new[] { T("Dinner time, I believe", "밥 시간인 것 같은데") },
            Sleepy: new[] { T("Sun spot nap", "햇볕 자리에서 낮잠") },
            Held: new[] { T("Fine, a short hug", "좋아, 짧게만 안아") },
            Rain: new[] { T("I'll watch it from the window", "창문으로 구경할게") },
            Snow: new[] { T("Cold! Back inside", "추워! 들어가자") },
            Called: new[] { T("Coming, mrrp", "간다, 므릅") }));

    public static readonly Breed NorwegianForest = new(
        "norwegian-forest", PetSpecies.Cat, T("Norwegian Forest Cat", "노르웨이숲"), T("Fluffy forest climber", "숲속 털뭉치 등반가"),
        T("A sturdy fluffball from the snowy woods with a lion's ruff and a bushy tail. Gentle and calm, but give it something high and it climbs like a squirrel.",
          "눈 덮인 숲에서 온 튼튼한 털뭉치, 사자 같은 목털과 풍성한 꼬리를 가졌어요. 순하고 차분하지만 높은 곳만 보이면 다람쥐처럼 올라가요."),
        0.6f, 0.3f, 0.65f, 0.55f, 0.65f,
        1.0f, 1.0f, 1.35f, 1.15f, 0.05f,
        0.1f, 1.0f, 0.2f, 1.0f, TravelMode.Free,
        new[] { T("Freya", "프레야"), T("Bjorn", "비욘"), T("Pine", "솔이"), T("Fjord", "피오르"), T("Moss", "이끼") },
        new BreedVoice(
            Greeting: new[] { T("Hello from the forest", "숲에서 인사할게") },
            Idle: new[] { T("Is there anything taller?", "더 높은 데 없나?"), T("My tail is magnificent", "내 꼬리 멋지지"), T("Quiet day in the woods", "숲은 오늘도 조용해"), T("Mrrrm", "므르르음") },
            Happy: new[] { T("Up, up, up!", "위로, 위로!") },
            Petted: new[] { T("Mind the fluff", "털 조심해"), T("Purrrr", "골골골") },
            Hungry: new[] { T("A forest cat needs fish", "숲 고양이는 생선이 필요해") },
            Sleepy: new[] { T("Curled up in my tail", "꼬리에 폭 싸여 잘게") },
            Held: new[] { T("I'd rather be on a tree", "나무 위가 더 좋은데") },
            Rain: new[] { T("My coat sheds water, mostly", "털이 물을 튕겨, 대체로") },
            Snow: new[] { T("Snow! Like home!", "눈이다! 고향 같아!") },
            Called: new[] { T("Climbing down, coming", "내려가는 중, 갈게") }));

    public static readonly Breed TurkishAngora = new(
        "turkish-angora", PetSpecies.Cat, T("Turkish Angora", "터키시 앙고라"), T("Graceful white dancer", "우아한 하얀 무용수"),
        T("A slender white cat with silky fur and sometimes odd-coloured eyes. Elegant but lively, it dances after toys, opens doors and likes to be part of everything.",
          "비단결 털에 가끔 짝짝이 눈을 가진 날씬한 하얀 고양이. 우아하지만 활발해서 장난감을 따라 춤추고, 문도 열고, 모든 일에 끼고 싶어 해요."),
        0.8f, 0.2f, 0.8f, 0.3f, 0.8f,
        0.95f, 1.2f, 1.3f, 0.9f, -0.05f,
        0.4f, 0.1f, 0.3f, 1.1f, TravelMode.Follow,
        new[] { T("Pearl", "진주"), T("Ivory", "상아"), T("Luna", "루나"), T("Snow", "설이"), T("Silk", "비단이") },
        new BreedVoice(
            Greeting: new[] { T("Mew! What are we doing?", "미야! 우리 뭐 해?") },
            Idle: new[] { T("Look at me twirl", "빙글 도는 거 봐"), T("What's behind that door?", "저 문 뒤엔 뭐가 있지?"), T("I'm helping!", "나 돕는 중!"), T("Mew mew", "미야미야") },
            Happy: new[] { T("Dance time!", "춤출 시간!") },
            Petted: new[] { T("Gently, I'm silk", "살살, 나는 비단이야") },
            Hungry: new[] { T("A dainty snack, please", "우아한 간식 주세요") },
            Sleepy: new[] { T("A short, graceful nap", "짧고 우아한 낮잠") },
            Held: new[] { T("Only for a moment!", "잠깐만이야!") },
            Rain: new[] { T("Hmm, water…", "흠, 물이네…") },
            Snow: new[] { T("I blend right in!", "나랑 똑같은 색이야!") },
            Called: new[] { T("Dancing over!", "춤추며 갈게!") }));

    public static readonly Breed ExoticShorthair = new(
        "exotic-shorthair", PetSpecies.Cat, T("Exotic Shorthair", "엑조틱 숏헤어"), T("Plush-toy couch potato", "봉제인형 집콕러"),
        T("A Persian face in a short plush coat, like a teddy bear that came to life. Quiet, sweet and wonderfully lazy, it follows you from room to room and then naps there.",
          "짧고 폭신한 털에 페르시안 얼굴, 살아 움직이는 곰인형 같아요. 조용하고 다정하고 아주 게을러서, 방마다 따라와서는 거기서 낮잠을 자요."),
        0.45f, 0.3f, 0.25f, 0.85f, 0.6f,
        1.2f, 0.8f, 0.6f, 1.1f, 0.25f,
        0.4f, 0f, 0f, 1.2f, TravelMode.Follow,
        new[] { T("Teddy", "테디"), T("Pudding", "푸딩"), T("Biscuit", "비스킷"), T("Bun", "찐빵"), T("Momo", "모모") },
        new BreedVoice(
            Greeting: new[] { T("Mew. Hello.", "먕. 안녕.") },
            Idle: new[] { T("I'm resting my eyes", "눈만 잠깐 쉬는 중"), T("Wherever you go, I go… slowly", "어디 가든 따라가… 천천히"), T("Squishy face, squishy heart", "말랑한 얼굴, 말랑한 마음"), T("Mrrm", "므음") },
            Happy: new[] { T("Mrrp, nice", "므릅, 좋다") },
            Petted: new[] { T("Mmm, yes, purr", "음, 좋아, 골골") },
            Hungry: new[] { T("Snack o'clock?", "간식 시간?") },
            Sleepy: new[] { T("Nap number five…", "다섯 번째 낮잠…") },
            Held: new[] { T("I'm a plush toy now", "나 이제 인형이야") },
            Rain: new[] { T("Good weather for napping", "낮잠 자기 좋은 날씨") },
            Snow: new[] { T("Too cold, back to the blanket", "추워, 담요로 갈래") },
            Called: new[] { T("Coming… in a minute", "가… 조금 이따") }));

    public static readonly IReadOnlyList<Breed> All = new[]
    {
        Pomeranian, Maltese, Poodle, Bichon, Shiba, Corgi, Jindo,
        GoldenRetriever, FrenchBulldog, Dachshund, SiberianHusky, Chihuahua, Beagle,
        Samoyed, BorderCollie, ShihTzu, Pug,
        KoreanShorthair, RussianBlue, BritishShorthair, ScottishFold, Persian, Ragdoll, Munchkin,
        Siamese, MaineCoon, Bengal, Sphynx, Abyssinian,
        AmericanShorthair, NorwegianForest, TurkishAngora, ExoticShorthair,
    };

    public static IEnumerable<Breed> Of(PetSpecies species) => All.Where(b => b.Species == species);

    /// <summary>Pets from before breeds existed: the grey tabby is a Korean shorthair, the orange dog a Pomeranian.</summary>
    public static Breed Default(PetSpecies species) => species == PetSpecies.Cat ? KoreanShorthair : Pomeranian;

    public static Breed Find(string? id, PetSpecies species) =>
        All.FirstOrDefault(b => b.Id == id && b.Species == species) ?? Default(species);
}
