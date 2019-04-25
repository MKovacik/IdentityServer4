// Copyright (c) Brock Allen & Dominick Baier. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.


using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using IdentityModel;
using IdentityServer4.Extensions;
using IdentityServer4.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace IdentityServer4.Validation
{
    /// <summary>
    /// Validates JWTs based on Secrets
    /// </summary>
    internal class JwtRequestValidator
    {
        private readonly string _audienceUri;
        private readonly ILogger _logger;

        /// <summary>
        /// Instantiates an instance of private_key_jwt secret validator
        /// </summary>
        public JwtRequestValidator(IHttpContextAccessor contextAccessor, ILogger<JwtRequestValidator> logger)
        {
            _audienceUri = contextAccessor.HttpContext.GetIdentityServerIssuerUri();
            _logger = logger;
        }

        internal JwtRequestValidator(string audience, ILogger<JwtRequestValidator> logger)
        {
            _audienceUri = audience;
            _logger = logger;
        }

        public Task<JwtRequestValidationResult> ValidateAsync(Client client, string jwtTokenString)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (String.IsNullOrWhiteSpace(jwtTokenString)) throw new ArgumentNullException(nameof(jwtTokenString));

            var fail = Task.FromResult(new JwtRequestValidationResult { IsError = true });

            var enumeratedSecrets = client.ClientSecrets.ToList().AsReadOnly();

            List<SecurityKey> trustedKeys; 
            try
            {
                trustedKeys = GetTrustedKeys(enumeratedSecrets);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Could not parse client secrets");
                return fail;
            }

            if (!trustedKeys.Any())
            {
                _logger.LogError("There are no keys available to validate JWT.");
                return fail;
            }

            var tokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKeys = trustedKeys,
                ValidateIssuerSigningKey = true,

                ValidIssuer = client.ClientId,
                ValidateIssuer = true,

                ValidAudience = _audienceUri,
                ValidateAudience = true,

                RequireSignedTokens = true,
                RequireExpirationTime = true
            };
            try
            {
                var handler = new JwtSecurityTokenHandler();
                handler.ValidateToken(jwtTokenString, tokenValidationParameters, out var token);

                var jwtSecurityToken = (JwtSecurityToken)token;

                // todo: IdentityModel update
                if (jwtSecurityToken.Payload.ContainsKey("request") ||
                    jwtSecurityToken.Payload.ContainsKey("request_uri"))
                {
                    _logger.LogError("JWT payload must not contain request or request_uri");
                    return fail;
                }

                // filter JWT validation values
                var payload = new Dictionary<string, string>();
                foreach(var key in jwtSecurityToken.Payload.Keys)
                {
                    if (!Constants.Filters.JwtRequestClaimTypesFilter.Contains(key))
                    {
                        var value = jwtSecurityToken.Payload[key];

                        if (value is string s)
                        {
                            payload.Add(key, s);
                        }
                        // todo: handle other types (convert arrays, JSON, etc.)
                    }
                }

                var result = new JwtRequestValidationResult
                {
                    IsError = false,
                    Payload = payload
                };

                return Task.FromResult(result);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "JWT token validation error");
                return fail;
            }
        }

        private List<SecurityKey> GetTrustedKeys(IReadOnlyCollection<Secret> secrets)
        {
            var trustedKeys = GetAllTrustedCertificates(secrets)
                                .Select(c => (SecurityKey)new X509SecurityKey(c))
                                .ToList();

            if (!trustedKeys.Any()
                && secrets.Any(s => s.Type == IdentityServerConstants.SecretTypes.X509CertificateThumbprint))
            {
                _logger.LogWarning("Client must be configured with X509CertificateBase64 secret.");
            }

            return trustedKeys;
        }

        private List<X509Certificate2> GetAllTrustedCertificates(IEnumerable<Secret> secrets)
        {
            return secrets
                .Where(s => s.Type == IdentityServerConstants.SecretTypes.X509CertificateBase64)
                .Select(s => GetCertificateFromString(s.Value))
                .Where(c => c != null)
                .ToList();
        }

        private X509Certificate2 GetCertificateFromString(string value)
        {
            try
            {
                return new X509Certificate2(Convert.FromBase64String(value));
            }
            catch
            {
                _logger.LogWarning("Could not read certificate from string: " + value);
                return null;
            }
        }
    }
}