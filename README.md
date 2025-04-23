# infer-tfmodule-schema

A tool for inferring the schema of TF modules, specifically the output types of the modules to be used as extra configuration with the [pulumi-terraform-module](https://github.com/pulumi/pulumi-terraform-module) provider. 

It uses OpenAI `gpt-4o` to analyze the module and generate a JSON schema for the output variables. As such, you will need an OpenAI API key to use this tool and it is expected to be set in the environment variable `OPENAI_KEY`.

### Installation

```
pulumi plugin install tool infer-tfmodule-schema
```


### Usage with remote modules

```
pulumi run infer-tfmodule-schema -- <module-source> <version> <output-file-name>
```

### Usage with local modules

```
pulumi run infer-tfmodule-schema -- <module-path> <output-file-name>
```