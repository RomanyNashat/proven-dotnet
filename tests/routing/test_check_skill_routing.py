import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("routing", ROOT / "tools" / "ci" / "check-skill-routing.py")
routing = importlib.util.module_from_spec(spec)
spec.loader.exec_module(routing)

SKILLS = {
    "redis-patterns": "redis patterns Redis with StackExchange.Redis: cache-aside, a token-checked lock, rate limiter",
    "kafka-patterns": "kafka patterns Kafka with Confluent.Kafka: idempotent producer, at-least-once consumer, offsets",
    "docker-dotnet": "docker dotnet Docker for .NET: multi-stage builds, small runtime images, layer caching, non-root users",
}


class RoutingTests(unittest.TestCase):
    def test_stem_is_the_same_for_both_forms(self):
        for a, b in [("caching", "cache"), ("layers", "layer"), ("smaller", "small"), ("policies", "policy")]:
            self.assertEqual(routing.stem(a), routing.stem(b), (a, b))

    def test_a_request_ranks_its_skill_first(self):
        docs, idf = routing.vectors(SKILLS)
        place, _ = routing.rank("make the image smaller and cache the restore layer", "docker-dotnet", docs, idf)
        self.assertEqual(1, place)

    def test_a_description_that_lost_its_words_drops(self):
        # Given a description edited down to something generic
        broken = dict(SKILLS, **{"docker-dotnet": "docker dotnet Containers."})
        docs, idf = routing.vectors(broken)
        # Then the request it used to win goes elsewhere
        place, _ = routing.rank("make the image smaller and cache the restore layer", "docker-dotnet", docs, idf)
        self.assertGreater(place, 1)


if __name__ == "__main__":
    unittest.main()
