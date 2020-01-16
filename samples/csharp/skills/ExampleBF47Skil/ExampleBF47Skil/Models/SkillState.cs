// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Luis;

namespace ExampleBF47Skill.Models
{
    public class SkillState
    {
        public string Token { get; set; }

        public ExampleBF47SkillLuis LuisResult { get; set; }

        public void Clear()
        {
        }
    }
}
